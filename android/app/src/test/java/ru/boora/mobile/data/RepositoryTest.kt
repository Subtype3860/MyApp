package ru.boora.mobile.data

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.flowOf
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.encodeToString
import org.junit.Assert.*
import org.junit.Test
import java.io.IOException

class RepositoryTest {
    private class Sessions : SessionAccess {
        override val current = MutableStateFlow<Session?>(Session("owner", "token", "worker"))
        override fun save(session: Session) { current.value = session }
        override fun clear() { current.value = null }
    }
    private class Store : LocalDao {
        val operations = mutableListOf<PendingOperation>()
        var cache: SnapshotEntity? = null
        override fun observeSnapshot(owner: String) = flowOf(cache?.takeIf { it.owner == owner })
        override suspend fun snapshot(owner: String) = cache?.takeIf { it.owner == owner }
        override suspend fun saveSnapshot(snapshot: SnapshotEntity) { cache = snapshot }
        override fun observeQueue(owner: String) = flowOf(operations.filter { it.owner == owner })
        override suspend fun queue(owner: String) = operations.filter { it.owner == owner }
        override suspend fun enqueue(operation: PendingOperation) { operations.add(operation) }
        override suspend fun mark(id: String, status: String, message: String) {
            val index = operations.indexOfFirst { it.id == id }
            operations[index] = operations[index].copy(status = status, message = message)
        }
        override suspend fun remove(id: String, owner: String) { operations.removeAll { it.id == id && it.owner == owner } }
    }
    private fun operation(owner: String = "owner") = PendingOperation("operation-id", owner, "vehicle-id", "Машина", wireJson.encodeToString(
        Completion(7, "2026-10-24", "Работы выполнены", operationId = "operation-id")), 1)
    private class Server : ApiTransport {
        val sent = mutableListOf<String>()
        var failure: Exception? = null
        var capabilitiesSupported = true
        override suspend fun request(path: String, token: String?, body: String?): String = when {
            path.endsWith("sync-capabilities") -> if (capabilitiesSupported) """{"completionIdempotency":1}""" else throw ApiFailure(404,"Not found")
            path.endsWith("/complete") -> { sent.add(body!!); failure?.let { throw it }; """{"revision":8}""" }
            else -> """{"canManage":true,"rows":[]}"""
        }
    }
    @Test fun lostResponseRetainsDurableOperationAndRetriesIdenticalPayload() = runTest {
        val store = Store().apply { operations.add(operation()) }
        val server = Server().apply { failure = IOException("lost response") }
        val repo = Repository(store, Sessions(), server)
        assertFalse(repo.sync())
        assertEquals("pending", store.operations.single().status)
        server.failure = null
        assertTrue(repo.sync())
        assertTrue(store.operations.isEmpty())
        assertEquals(2, server.sent.size)
        assertEquals(server.sent[0], server.sent[1])
    }
    @Test fun conflictRetainsDescriptionAndNeverRebasesOrResendsAutomatically() = runTest {
        val store = Store().apply { operations.add(operation()) }
        val server = Server().apply { failure = ApiFailure(409, "График изменён") }
        val repo = Repository(store, Sessions(), server)
        assertTrue(repo.sync())
        assertEquals("conflict", store.operations.single().status)
        assertTrue(store.operations.single().payload.contains("Работы выполнены"))
        assertTrue(repo.sync())
        assertEquals(1, server.sent.size)
    }
    @Test fun expiredTokenDoesNotDiscardOperationOrCachedData() = runTest {
        val store = Store().apply { operations.add(operation()); cache = SnapshotEntity("owner", "cached", 1) }
        val server = Server().apply { failure = ApiFailure(401, "Войдите снова") }
        val sessions = Sessions()
        val repository = Repository(store, sessions, server)
        assertTrue(repository.sync())
        assertTrue(repository.authenticationRequired.value)
        assertTrue(sessions.current.value!!.reauthenticationRequired)
        val restarted = Repository(store, sessions, server)
        assertTrue(restarted.authenticationRequired.value)
        val count = server.sent.size
        restarted.sync()
        assertEquals(count, server.sent.size)
        assertEquals("pending", store.operations.single().status)
        assertEquals("cached", store.cache!!.json)
    }
    @Test fun anotherAccountsOperationsAreNeverSent() = runTest {
        val store = Store().apply { operations.add(operation("another-user")) }
        val server = Server()
        assertTrue(Repository(store, Sessions(), server).sync())
        assertTrue(server.sent.isEmpty())
        assertEquals(1, store.operations.size)
    }
    @Test fun oldServerCannotReceiveUnprotectedOfflineWrites() = runTest {
        val store = Store().apply { operations.add(operation()) }
        val server = Server().apply { capabilitiesSupported = false }
        assertTrue(Repository(store, Sessions(), server).sync())
        assertTrue(server.sent.isEmpty())
        assertEquals(1, store.operations.size)
    }
    @Test fun uncertainOperationCannotBeDiscardedAndRecreated() = runTest {
        val store = Store().apply { operations.add(operation()) }
        val repo = Repository(store, Sessions(), Server())
        try { repo.discard("operation-id"); fail("Pending operation was discarded") }
        catch (_: IllegalStateException) { }
        assertEquals(1, store.operations.size)
    }
    @Test fun reauthenticationResumesSameAccountButCannotReplaceOwnerWithPendingOperations() = runTest {
        val store = Store().apply { operations.add(operation()) }
        val sessions = Sessions().apply { save(current.value!!.copy(reauthenticationRequired = true)) }
        var loginOwner = "other"
        val server = object : ApiTransport {
            override suspend fun request(path: String, token: String?, body: String?): String {
                assertEquals("/api/auth/login", path)
                val payload = java.util.Base64.getUrlEncoder().withoutPadding().encodeToString("""{"sub":"$loginOwner"}""".toByteArray())
                return wireJson.encodeToString(AuthResponse("header.$payload.signature", "user", emptyList()))
            }
        }
        val repo = Repository(store, sessions, server)
        assertTrue(repo.authenticationRequired.value)
        try { repo.login("other", "synthetic"); fail() } catch (_: IllegalStateException) { }
        assertEquals("owner", sessions.current.value!!.owner)
        assertTrue(repo.authenticationRequired.value)
        loginOwner = "owner"; repo.login("worker", "synthetic")
        assertFalse(repo.authenticationRequired.value)
        assertFalse(sessions.current.value!!.reauthenticationRequired)
        assertEquals(1, store.operations.size)
    }
}
