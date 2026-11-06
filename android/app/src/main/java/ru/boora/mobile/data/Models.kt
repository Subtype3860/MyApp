package ru.boora.mobile.data

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject

val wireJson = Json { ignoreUnknownKeys = true; encodeDefaults = true }

@Serializable data class LoginRequest(val userNameOrEmail: String, val password: String)
@Serializable data class AuthResponse(val token: String, val role: String, val permissions: List<String>)
@Serializable data class Vehicle(
    val id: String, val groupName: String = "", val typeName: String = "",
    val modelName: String = "", val garageNumber: Int? = null,
    val stateNumber: String = "", val operationalState: String = "healthy",
    val engineCount: Int = 1, val isArchived: Boolean = false,
) {
    val title get() = listOfNotNull(modelName.ifBlank { typeName }, garageNumber?.let { "№ $it" }).joinToString(" · ")
}
@Serializable data class Due(
    val kind: String, val runInId: String? = null, val name: String,
    val engineNumber: Int, val currentHours: Double, val dueHours: Double,
    val remaining: Double, val readingDate: String, val isEstimated: Boolean = false,
)
@Serializable data class MaintenanceRow(
    val vehicle: Vehicle, val revision: Int, val plan: JsonObject? = null,
    val due: List<Due> = emptyList(), val incomplete: Boolean = false,
)
@Serializable data class MaintenanceSnapshot(val canManage: Boolean, val rows: List<MaintenanceRow>)
@Serializable data class Completion(
    val revision: Int, val date: String, val description: String,
    val runInId: String? = null, val engines: List<JsonObject> = emptyList(),
    val operationId: String,
)
