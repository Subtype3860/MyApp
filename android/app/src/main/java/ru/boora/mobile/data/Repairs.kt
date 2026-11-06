package ru.boora.mobile.data

import androidx.room.*
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import java.time.OffsetDateTime
import java.util.UUID

@Serializable data class RepairDefect(
    val id: String, val symptoms: String = "", val status: String = "new",
    val nodeName: String = "", val failureReason: String = "", val operationalState: String = "stopped",
    val createdAt: String = "", val assignedToId: String? = null, val assignedToName: String = "",
    val downtimeStartedAt: String? = null,
)
@Serializable data class RepairWork(
    val id: String, val defectId: String? = null, val description: String = "", val status: String = "",
    val cause: String = "", val requiredParts: String = "", val location: String = "",
    val completedAt: String? = null, val createdAt: String = "", val performerName: String = "",
    val operationalState: String? = null,
)
@Serializable data class RepairJournal(val vehicle: Vehicle, val defects: List<RepairDefect> = emptyList(), val works: List<RepairWork> = emptyList())
@Serializable data class RepairRow(val journal: RepairJournal, val version: String)
@Serializable data class RepairSnapshot(val protocol: Int, val rows: List<RepairRow>)
@Serializable data class RepairWorkInput(
    val defectId: String, val cause: String, val description: String, val status: String,
    val requiredParts: String, val repairDateTime: String, val operationalState: String,
    val downtimeStartedAt: String? = null, val location: String,
)
@Serializable data class RepairCommand(
    val operationId: String, val vehicleId: String, val defectId: String, val version: String,
    val kind: String, val work: RepairWorkInput? = null,
)
@Entity(tableName = "repair_snapshots")
data class RepairCache(@PrimaryKey val owner: String, val json: String, val fetchedAt: Long)
@Entity(tableName = "repair_outbox", indices = [Index(value = ["owner", "vehicleId"], unique = true)])
data class RepairPending(
    @PrimaryKey val id: String, val owner: String, val vehicleId: String, val title: String,
    val payload: String, val createdAt: Long, val status: String = "pending", val message: String = "Ожидает отправки",
)
@Dao interface RepairDao {
    @Query("SELECT * FROM repair_snapshots WHERE owner = :owner") fun observe(owner: String): Flow<RepairCache?>
    @Query("SELECT * FROM repair_snapshots WHERE owner = :owner") suspend fun cache(owner: String): RepairCache?
    @Upsert suspend fun save(cache: RepairCache)
    @Query("DELETE FROM repair_snapshots WHERE owner = :owner") suspend fun clear(owner: String)
    @Query("SELECT * FROM repair_outbox WHERE owner = :owner ORDER BY createdAt") fun observeQueue(owner: String): Flow<List<RepairPending>>
    @Query("SELECT * FROM repair_outbox WHERE owner = :owner ORDER BY createdAt") suspend fun queue(owner: String): List<RepairPending>
    @Insert suspend fun insert(operation: RepairPending)
    @Query("UPDATE repair_outbox SET status = :status, message = :message WHERE id = :id AND owner = :owner") suspend fun mark(id: String, owner: String, status: String, message: String)
    @Query("DELETE FROM repair_outbox WHERE id = :id AND owner = :owner") suspend fun remove(id: String, owner: String)
}

class RepairRepository(val dao: RepairDao, private val sessions: SessionAccess, private val api: ApiTransport,
    private val onAuthenticationRequired: () -> Unit = {}) {
    private val lock = Mutex()
    val message = MutableStateFlow("Загрузите журнал ремонта при наличии сети")
    suspend fun enqueue(row: RepairRow, defect: RepairDefect, work: RepairWorkInput? = null) = lock.withLock {
        val session = sessions.current.value ?: error("Требуется вход")
        val snapshot = dao.cache(session.owner)?.let { wireJson.decodeFromString<RepairSnapshot>(it.json) } ?: error("Сначала загрузите журнал")
        check(snapshot.protocol == 1) { "Обновите приложение" }
        val current = snapshot.rows.singleOrNull { it.journal.vehicle.id == row.journal.vehicle.id } ?: error("Техника не найдена")
        check(current.version == row.version && !current.journal.vehicle.isArchived) { "Журнал обновлён. Откройте карточку заново" }
        val selected = current.journal.defects.singleOrNull { it.id == defect.id } ?: error("Неисправность не найдена")
        check(selected.status != "repaired") { "Ремонт уже завершён" }
        check(dao.queue(session.owner).none { it.vehicleId == row.journal.vehicle.id }) { "Сначала отправьте предыдущую операцию этой машины" }
        if (work == null) check(selected.assignedToId == null && selected.status == "new") { "Заявка уже взята в работу" }
        else validateRepair(work, defect.id)
        val id = UUID.randomUUID().toString()
        val command = RepairCommand(id, row.journal.vehicle.id, defect.id, row.version, if (work == null) "claim" else "work", work)
        dao.insert(RepairPending(id, session.owner, row.journal.vehicle.id, row.journal.vehicle.title, wireJson.encodeToString(command), System.currentTimeMillis()))
        message.value = "Запись сохранена на телефоне. Результат подтвердит сервер после синхронизации"
    }
    suspend fun sync(): Boolean = lock.withLock {
        val session = sessions.current.value ?: return@withLock true
        try {
            // This read is also the protocol/permission check. Never fall back to legacy unsafe POST.
            var json = api.request("/api/repair-sync", session.token)
            check(wireJson.decodeFromString<RepairSnapshot>(json).protocol == 1) { "Обновите приложение: версия протокола ремонта не поддерживается" }
            for (op in dao.queue(session.owner).filter { it.status == "pending" }) {
                try {
                    api.request("/api/repair-sync/commands", session.token, op.payload)
                    dao.remove(op.id, session.owner)
                } catch (e: ApiFailure) {
                    when (disposition(e.code)) {
                        SyncDisposition.AUTHENTICATE, SyncDisposition.RETRY -> throw e
                        SyncDisposition.CONFLICT -> dao.mark(op.id, session.owner, "conflict", e.message)
                        SyncDisposition.REJECTED -> dao.mark(op.id, session.owner, "rejected", e.message)
                    }
                }
            }
            // Refresh only after commands: old snapshot tokens must not be reused after a successful write.
            json = api.request("/api/repair-sync", session.token)
            check(wireJson.decodeFromString<RepairSnapshot>(json).protocol == 1)
            dao.save(RepairCache(session.owner, json, System.currentTimeMillis()))
            message.value = if (dao.queue(session.owner).isEmpty()) "Журнал ремонта обновлён" else "Есть записи для проверки. Откройте очередь ремонта"
            true
        } catch (e: CancellationException) { throw e }
        catch (e: ApiFailure) {
            if (e.code == 401) onAuthenticationRequired()
            if (e.code == 403) dao.clear(session.owner)
            message.value = if (e.code == 404) "Обновите сервер для работы с ремонтом. Локальные записи сохранены" else e.message
            disposition(e.code) != SyncDisposition.RETRY
        } catch (e: java.io.IOException) { message.value = "Нет связи. Показана сохранённая копия; записи ремонта остаются в очереди"; false }
        catch (e: Exception) { message.value = "Не удалось прочитать журнал ремонта. Локальные записи сохранены"; true }
    }
    suspend fun discard(id: String) = lock.withLock {
        val owner = sessions.current.value?.owner ?: return@withLock
        val item = dao.queue(owner).singleOrNull { it.id == id } ?: return@withLock
        check(item.status in listOf("conflict", "rejected")) { "Неизвестный результат отправки: повторите синхронизацию" }
        dao.remove(id, owner)
    }
}

fun validateRepair(work: RepairWorkInput, defectId: String) {
    require(work.defectId == defectId)
    require(work.description.trim().length in 1..4000 && work.cause.trim().length in 1..4000) { "Заполните выполненные работы и причину (до 4000 символов)" }
    require(work.location in listOf("КАРЬЕР", "РММ")) { "Выберите место ремонта" }
    require(work.status in listOf("repaired", "faulty", "awaiting_parts")) { "Выберите результат ремонта" }
    require(work.requiredParts.length <= 4000 && (work.status != "awaiting_parts" || work.requiredParts.isNotBlank())) { "Укажите необходимые запчасти" }
    require(work.operationalState in listOf("operating", "stopped"))
    val completed = runCatching { OffsetDateTime.parse(work.repairDateTime) }.getOrNull()
    require(completed != null && !completed.isAfter(OffsetDateTime.now().plusMinutes(5))) { "Некорректное время ремонта" }
    if (work.operationalState == "stopped") {
        val stopped = runCatching { OffsetDateTime.parse(work.downtimeStartedAt) }.getOrNull()
        require(stopped != null && !stopped.isAfter(OffsetDateTime.now().plusMinutes(5))) { "Укажите время начала простоя" }
    }
}
