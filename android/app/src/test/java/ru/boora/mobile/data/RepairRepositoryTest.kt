package ru.boora.mobile.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.encodeToString
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

class RepairRepositoryTest {
    private class Sessions : SessionAccess {
        override val current = MutableStateFlow<Session?>(Session("owner", "token", "worker"))
        override fun save(session: Session) { current.value = session }
        override fun clear() { current.value = null }
    }
    private class Store : RepairDao {
        val pending = mutableListOf<RepairPending>()
        var snapshot: RepairCache? = null
        override fun observe(owner: String) = flowOf(snapshot?.takeIf { it.owner == owner })
        override suspend fun cache(owner: String) = snapshot?.takeIf { it.owner == owner }
        override suspend fun save(cache: RepairCache) { snapshot = cache }
        override suspend fun clear(owner: String) { if (snapshot?.owner == owner) snapshot = null }
        override fun observeQueue(owner: String) = flowOf(pending.filter { it.owner == owner })
        override suspend fun queue(owner: String) = pending.filter { it.owner == owner }
        override suspend fun insert(operation: RepairPending) { pending.add(operation) }
        override suspend fun mark(id: String, owner: String, status: String, message: String) {
            val i = pending.indexOfFirst { it.id == id && it.owner == owner }
            pending[i] = pending[i].copy(status = status, message = message)
        }
        override suspend fun remove(id: String, owner: String) { pending.removeAll { it.id == id && it.owner == owner } }
    }
    private val defect = RepairDefect("defect", "Шум", "new")
    private val row = RepairRow(RepairJournal(Vehicle("vehicle"), listOf(defect)), "A".repeat(64))
    private fun store() = Store().apply { snapshot = RepairCache("owner", wireJson.encodeToString(RepairSnapshot(1, listOf(row))), 1) }
    private fun work() = RepairWorkInput("defect", "Износ", "Замена", "awaiting_parts", "Фильтр", "2026-01-01T10:00:00+11:00", "operating", location = "РММ")
    private class Server(val json: String) : ApiTransport {
        var readError: Exception? = null
        var writeError: Exception? = null
        val sent = mutableListOf<Pair<String?, String>>()
        override suspend fun request(path: String, token: String?, body: String?): String {
            if (path.endsWith("/commands")) {
                sent.add(token to body!!); writeError?.let { throw it }; return "{\"id\":\"work\"}"
            }
            assertEquals("/api/repair-sync", path)
            readError?.let { throw it }; return json
        }
    }
    @Test fun lossAfterCommitAndRestartResendsIdenticalCommand() = runTest {
        val store = store(); val session = Sessions(); val server = Server(store.snapshot!!.json)
        val repo = RepairRepository(store, session, server)
        repo.enqueue(row, defect, work())
        val original = store.pending.single().payload
        server.writeError = IOException("lost response")
        assertFalse(repo.sync()); assertEquals("pending", store.pending.single().status)
        server.writeError = null
        assertTrue(RepairRepository(store, session, server).sync())
        assertTrue(store.pending.isEmpty()); assertEquals(listOf(original, original), server.sent.map { it.second })
    }
    @Test fun conflictsKeepTextAndNeverAutomaticallyReplayAsNewOperation() = runTest {
        val store = store(); val server = Server(store.snapshot!!.json).apply { writeError = ApiFailure(409, "Журнал изменён") }
        val repo = RepairRepository(store, Sessions(), server)
        repo.enqueue(row, defect, work()); assertTrue(repo.sync())
        assertEquals("conflict", store.pending.single().status)
        assertTrue(store.pending.single().payload.contains("Замена"))
        repo.sync(); assertEquals(1, server.sent.size)
        repo.discard(store.pending.single().id); assertTrue(store.pending.isEmpty())
    }
    @Test fun oldServerAndMissingNetworkKeepQueueAndLocalCopy() = runTest {
        val store = store(); val server = Server(store.snapshot!!.json)
        val repo = RepairRepository(store, Sessions(), server)
        repo.enqueue(row, defect)
        server.readError = ApiFailure(404, "old server"); assertTrue(repo.sync())
        server.readError = IOException(); assertFalse(repo.sync())
        assertNotNull(store.snapshot); assertEquals(1, store.pending.size); assertTrue(server.sent.isEmpty())
    }
    @Test fun revokedSessionDoesNotDiscardUncertainWrite() = runTest {
        val store = store(); val server = Server(store.snapshot!!.json).apply { writeError = ApiFailure(401, "Войдите снова") }
        var needsLogin = false
        val repo = RepairRepository(store, Sessions(), server) { needsLogin = true }
        repo.enqueue(row, defect); repo.sync()
        assertTrue(needsLogin)
        assertEquals("pending", store.pending.single().status)
        try { repo.discard(store.pending.single().id); fail() } catch (_: IllegalStateException) { }
    }
    @Test fun otherAccountNeverSendsPreviousOwnersQueueAndForbiddenClearsItsCache() = runTest {
        val store = store(); val session = Sessions(); val server = Server(store.snapshot!!.json)
        val repo = RepairRepository(store, session, server)
        repo.enqueue(row, defect)
        session.save(Session("other", "other-token", "other")); repo.sync()
        assertTrue(server.sent.isEmpty()); assertEquals("owner", store.pending.single().owner)
        server.readError = ApiFailure(403, "Нет доступа"); repo.sync()
        assertNull(store.snapshot); assertEquals(1, store.pending.size)
    }
    @Test fun missingHistoryStaleVersionAndDuplicateVehicleAreRejected() = runTest {
        val store = store(); val repo = RepairRepository(store, Sessions(), Server(store.snapshot!!.json))
        try { repo.enqueue(row.copy(version = "B".repeat(64)), defect); fail() } catch (_: IllegalStateException) { }
        repo.enqueue(row, defect)
        try { repo.enqueue(row, defect); fail() } catch (_: IllegalStateException) { }
        store.snapshot = null
        try { repo.enqueue(row, defect); fail() } catch (_: IllegalStateException) { }
    }
    @Test fun repairRequiresPartsAndDowntimeOnlyWhenApplicable() {
        validateRepair(work(), "defect")
        listOf(work().copy(requiredParts = ""), work().copy(cause = ""), work().copy(operationalState = "stopped"),
            work().copy(location = ""), work().copy(repairDateTime = "bad"), work().copy(status = "unknown")).forEach {
            try { validateRepair(it, "defect"); fail() } catch (_: IllegalArgumentException) { }
        }
    }
}
