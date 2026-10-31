package ru.boora.mobile.data

import org.junit.Assert.*
import org.junit.Test
import kotlinx.serialization.encodeToString

class SyncPolicyTest {
    @Test fun expiredSessionAndConflictsAreNotRetriedAsNetworkFailures() {
        assertEquals(SyncDisposition.AUTHENTICATE, disposition(401))
        assertEquals(SyncDisposition.CONFLICT, disposition(409))
        assertEquals(SyncDisposition.REJECTED, disposition(403))
        assertEquals(SyncDisposition.REJECTED, disposition(400))
        for (code in listOf(408, 429, 500, 503)) assertEquals(SyncDisposition.RETRY, disposition(code))
    }
    @Test fun persistedCompletionPreservesIdAndRevisionForRetries() {
        val original = Completion(17, "2026-10-24", "Замена масла", operationId = "1e406b53-8b84-411a-a6c5-9d97542b3526")
        val restored = wireJson.decodeFromString<Completion>(wireJson.encodeToString(original))
        assertEquals(original, restored)
        assertEquals(17, restored.revision)
        assertEquals(original.operationId, restored.operationId)
    }
    @Test fun snapshotAcceptsAdditionalServerFieldsAndMissingPlan() {
        val snapshot = wireJson.decodeFromString<MaintenanceSnapshot>("""{"canManage":false,"rows":[{"vehicle":{"id":"v1","modelName":"Экскаватор","garageNumber":10,"futureField":true},"revision":0,"plan":null,"readings":[],"due":[],"incomplete":false}]}""")
        assertFalse(snapshot.canManage)
        assertNull(snapshot.rows.single().plan)
        assertEquals("Экскаватор · № 10", snapshot.rows.single().vehicle.title)
    }
}
