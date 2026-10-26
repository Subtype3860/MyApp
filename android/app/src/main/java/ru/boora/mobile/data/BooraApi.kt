package ru.boora.mobile.data

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.json.*
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import java.util.concurrent.TimeUnit

class ApiFailure(val code: Int, override val message: String) : Exception(message)

interface ApiTransport {
    suspend fun request(path: String, token: String? = null, body: String? = null): String
}

class BooraApi : ApiTransport {
    private val client = OkHttpClient.Builder().connectTimeout(15, TimeUnit.SECONDS)
        .readTimeout(30, TimeUnit.SECONDS).callTimeout(45, TimeUnit.SECONDS)
        .followRedirects(false).build()
    override suspend fun request(path: String, token: String?, body: String?): String = withContext(Dispatchers.IO) {
        require(path.startsWith("/api/"))
        val builder = Request.Builder().url("https://boora.ru$path").header("Accept", "application/json")
        token?.let { builder.header("Authorization", "Bearer $it") }
        body?.let { builder.post(it.toRequestBody("application/json".toMediaType())) }
        client.newCall(builder.build()).execute().use { response ->
            val text = response.body?.string().orEmpty()
            if (!response.isSuccessful) {
                val message = runCatching { wireJson.parseToJsonElement(text).jsonObject["message"]?.jsonPrimitive?.content }.getOrNull()
                throw ApiFailure(response.code, message ?: when (response.code) {
                    401 -> "Войдите снова для синхронизации"
                    403 -> "Нет доступа к операции"
                    409 -> "Данные изменены на сервере. Нужна проверка"
                    else -> "Сервер вернул ошибку ${response.code}"
                })
            }
            text
        }
    }
}
