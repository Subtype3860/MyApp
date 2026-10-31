package ru.boora.mobile.data

import java.util.Base64
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.*
import java.time.LocalDate
import java.time.ZoneId
import java.util.UUID

enum class SyncDisposition { AUTHENTICATE, CONFLICT, REJECTED, RETRY }
fun disposition(code: Int) = when {
    code == 401 -> SyncDisposition.AUTHENTICATE
    code == 409 -> SyncDisposition.CONFLICT
    code == 408 || code == 429 || code >= 500 -> SyncDisposition.RETRY
    else -> SyncDisposition.REJECTED
}

class Repository(val dao: LocalDao, val sessions: SessionAccess, private val api: ApiTransport, repairDao: RepairDao? = null) {
    val authenticationRequired = MutableStateFlow(sessions.current.value?.reauthenticationRequired == true)
    val repairs = repairDao?.let { RepairRepository(it, sessions, api, ::requireAuthentication) }
    private fun requireAuthentication() {
        authenticationRequired.value = true
        sessions.current.value?.let { sessions.save(it.copy(reauthenticationRequired = true)) }
    }
    private val lock = Mutex()
    val message = MutableStateFlow("Данные доступны после первого входа и синхронизации")
    val busy = MutableStateFlow(false)
    suspend fun login(name: String, password: String) = lock.withLock {
        val auth = wireJson.decodeFromString<AuthResponse>(api.request("/api/auth/login", body = wireJson.encodeToString(LoginRequest(name.trim(), password))))
        val claims = wireJson.parseToJsonElement(String(Base64.getUrlDecoder().decode(auth.token.split('.')[1]), Charsets.UTF_8)).jsonObject
        val owner = (claims["sub"] ?: claims["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier"])?.jsonPrimitive?.content
            ?: error("Сервер не вернул идентификатор пользователя")
        val old = sessions.current.value
        check(old == null || old.owner == owner || (dao.queue(old.owner).isEmpty() && repairs?.dao?.queue(old.owner).orEmpty().isEmpty())) {
            "Сначала отправьте изменения текущего пользователя"
        }
        sessions.save(Session(owner, auth.token, name.trim()))
        authenticationRequired.value = false
        message.value = "Вход выполнен. Обновите данные"
    }
    suspend fun logout() = lock.withLock {
        val session = sessions.current.value ?: return@withLock
        check(dao.queue(session.owner).isEmpty()) { "Есть неотправленные изменения. Сначала синхронизируйте их" }
        check(repairs?.dao?.queue(session.owner).orEmpty().isEmpty()) { "Есть записи ремонта. Сначала обработайте очередь" }
        // Online revocation is required: do not imply that a server session was revoked while offline.
        try { api.request("/api/auth/logout", session.token, "{}") }
        catch (e: ApiFailure) { if (e.code != 401) throw e }
        sessions.clear()
        authenticationRequired.value = false
    }
    suspend fun enqueue(row: MaintenanceRow, date: String, description: String, runInId: String?) = lock.withLock {
        val session = sessions.current.value ?: error("Требуется вход")
        val snapshot = dao.snapshot(session.owner)?.let { wireJson.decodeFromString<MaintenanceSnapshot>(it.json) }
            ?: error("Сначала загрузите данные")
        check(snapshot.canManage) { "Нет прав на регистрацию ТО" }
        val latest = snapshot.rows.singleOrNull { it.vehicle.id == row.vehicle.id } ?: error("Техника не найдена")
        check(latest.revision == row.revision && latest.plan != null) { "График обновлён. Откройте карточку заново" }
        val parsed = runCatching { LocalDate.parse(date) }.getOrNull()
        require(parsed != null) { "Введите корректную дату в формате ГГГГ-ММ-ДД" }
        require(!parsed.isAfter(LocalDate.now(ZoneId.of("Asia/Sakhalin")))) { "Дата не может быть в будущем" }
        require(description.isNotBlank() && description.length <= 5000) { "Заполните описание, не более 5000 символов" }
        check(dao.queue(session.owner).none { it.vehicleId == row.vehicle.id }) { "Для этой машины уже есть неотправленное ТО" }
        val id = UUID.randomUUID().toString()
        val payload = Completion(row.revision, date, description.trim(), runInId, operationId = id)
        dao.enqueue(PendingOperation(id, session.owner, row.vehicle.id, row.vehicle.title, wireJson.encodeToString(payload), System.currentTimeMillis()))
        message.value = "ТО сохранено на телефоне. Сервер проверит дату и моточасы при синхронизации"
    }
    /** Returns false for transient failures so WorkManager retries with backoff. */
    suspend fun sync(): Boolean = lock.withLock {
        val session = sessions.current.value ?: return@withLock true
        if (authenticationRequired.value) return@withLock true
        busy.value = true
        var repairSynced = true
        try {
            repairSynced = repairs?.sync() ?: true
            if (authenticationRequired.value) return@withLock true
            val pending = dao.queue(session.owner).filter { it.status == "pending" }
            if (pending.isNotEmpty()) {
                val capabilities = try { wireJson.parseToJsonElement(api.request("/api/maintenance-plans/sync-capabilities", session.token)).jsonObject }
                    catch (e: ApiFailure) {
                        if (e.code == 404) { message.value = "Серверу требуется обновление для офлайн-синхронизации. Записи сохранены"; return@withLock true }
                        throw e
                    }
                check(capabilities["completionIdempotency"]?.jsonPrimitive?.int == 1) { "Несовместимая версия синхронизации сервера" }
            }
            for (operation in pending) {
                try {
                    api.request("/api/maintenance-plans/${operation.vehicleId}/complete", session.token, operation.payload)
                    dao.remove(operation.id, session.owner)
                } catch (e: ApiFailure) {
                    when (disposition(e.code)) {
                        SyncDisposition.AUTHENTICATE -> throw e
                        SyncDisposition.RETRY -> throw e
                        SyncDisposition.CONFLICT -> dao.mark(operation.id, "conflict", e.message)
                        SyncDisposition.REJECTED -> dao.mark(operation.id, "rejected", e.message)
                    }
                }
            }
            val json = api.request("/api/maintenance-plans", session.token)
            wireJson.decodeFromString<MaintenanceSnapshot>(json)
            dao.saveSnapshot(SnapshotEntity(session.owner, json, System.currentTimeMillis()))
            message.value = if (dao.queue(session.owner).isEmpty()) "Данные синхронизированы" else "Есть записи, требующие проверки. Откройте очередь"
            repairSynced
        } catch (e: CancellationException) { throw e }
        catch (e: ApiFailure) {
            if (e.code == 401) requireAuthentication()
            message.value = e.message
            repairSynced && disposition(e.code) != SyncDisposition.RETRY
        } catch (e: java.io.IOException) {
            message.value = "Нет связи с сервером. Локальные данные и очередь сохранены"
            false
        } catch (e: Exception) {
            message.value = e.message ?: "Не удалось синхронизировать данные"
            true
        } finally { busy.value = false }
    }
    // Only definitively rejected operations can be removed. An uncertain network result
    // must be retried with the SAME operation id; never create a duplicate after a timeout.
    suspend fun discard(id: String) = lock.withLock {
        val owner = sessions.current.value?.owner ?: return@withLock
        val item = dao.queue(owner).singleOrNull { it.id == id } ?: return@withLock
        check(item.status in listOf("conflict", "rejected")) { "Сначала проверьте результат отправки" }
        dao.remove(id, owner)
    }
}
