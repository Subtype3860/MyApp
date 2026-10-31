package ru.boora.mobile

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.selection.SelectionContainer
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import ru.boora.mobile.data.*
import java.time.*
import java.time.format.DateTimeFormatter

private fun repairLabel(status: String) = when (status) {
    "repaired" -> "Исправен"
    "faulty" -> "Неисправен"
    "awaiting_parts" -> "Ожидание запчастей"
    "in_progress" -> "В работе"
    else -> "Новая заявка"
}
private fun stamp(value: String?): String = runCatching {
    OffsetDateTime.parse(value).atZoneSameInstant(ZoneId.of("Asia/Sakhalin")).format(DateTimeFormatter.ofPattern("dd.MM.yyyy HH:mm"))
}.getOrDefault(value.orEmpty())

@Composable fun RepairScreen(vm: MainViewModel) {
    val session by vm.session.collectAsStateWithLifecycle()
    val owner = session?.owner ?: return
    val repo = vm.repo.repairs ?: return
    val cache by remember(owner) { repo.dao.observe(owner) }.collectAsStateWithLifecycle(initialValue = null)
    val queued by remember(owner) { repo.dao.observeQueue(owner) }.collectAsStateWithLifecycle(initialValue = emptyList())
    val message by repo.message.collectAsStateWithLifecycle()
    val acting by vm.acting.collectAsStateWithLifecycle()
    val syncing by vm.repo.busy.collectAsStateWithLifecycle()
    val snapshot = remember(cache, owner) { cache?.takeIf { it.owner == owner }?.let { wireJson.decodeFromString<RepairSnapshot>(it.json) } }
    val queue = queued.filter { it.owner == owner }
    var mode by rememberSaveable(owner) { mutableStateOf("open") }
    var query by rememberSaveable(owner) { mutableStateOf("") }
    var selected by rememberSaveable(owner) { mutableStateOf<String?>(null) }
    var editing by rememberSaveable(owner) { mutableStateOf(false) }
    var claim by remember { mutableStateOf(false) }
    var discard by remember { mutableStateOf<RepairPending?>(null) }
    val rows = snapshot?.rows.orEmpty()
    val row = rows.find { it.journal.defects.any { d -> d.id == selected } }
    val defect = row?.journal?.defects?.find { it.id == selected }
    BackHandler(selected != null) { selected = null; editing = false }
    Column(Modifier.fillMaxSize()) {
        Text(message, Modifier.padding(horizontal = 16.dp, vertical = 6.dp), style = MaterialTheme.typography.bodySmall)
        cache?.takeIf { it.owner == owner }?.let {
            Text("Сохранённая копия: ${stamp(Instant.ofEpochMilli(it.fetchedAt).atOffset(ZoneOffset.UTC).toString())} · Сахалин",
                Modifier.padding(horizontal = 16.dp), style = MaterialTheme.typography.labelSmall)
        }
        if (row != null && defect != null) {
            val pending = queue.any { it.vehicleId == row.journal.vehicle.id }
            LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                item { TextButton(onClick = { selected = null }) { Text("Назад к неисправностям") } }
                item { Text(row.journal.vehicle.title, style = MaterialTheme.typography.headlineSmall) }
                item { Text(repairLabel(defect.status), style = MaterialTheme.typography.titleMedium)
                    SelectionContainer { Text(defect.symptoms) }
                    if (defect.nodeName.isNotBlank()) Text("Узел: ${defect.nodeName}")
                    Text("Зарегистрировано: ${stamp(defect.createdAt)}")
                    Text(if (defect.operationalState == "operating") "Эксплуатация продолжается" else "Машина остановлена")
                    defect.downtimeStartedAt?.let { Text("Простой с ${stamp(it)}") }
                    if (defect.assignedToId != null) Text("Исполнитель: ${defect.assignedToName}")
                }
                item {
                    if (pending) Text("Есть локальная операция этой машины. Сначала синхронизируйте или проверьте очередь ремонта")
                    if (defect.status == "new" && defect.assignedToId == null)
                        OutlinedButton(onClick = { claim = true }, enabled = !pending && !acting && !syncing) { Text("Взять в работу") }
                    if (defect.status != "repaired")
                        Button(onClick = { editing = true }, enabled = !pending && !acting && !syncing) { Text("Записать ремонт") }
                }
                item { Text("История работ", style = MaterialTheme.typography.titleLarge) }
                val works = row.journal.works.filter { it.defectId == defect.id }.sortedByDescending { it.completedAt ?: it.createdAt }
                if (works.isEmpty()) item { Text("Работы ещё не зарегистрированы") }
                items(works, key = { it.id }) { work ->
                    Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Text("${stamp(work.completedAt ?: work.createdAt)} · ${repairLabel(work.status)}")
                        SelectionContainer { Text(work.description) }
                        if (work.cause.isNotBlank()) Text("Причина: ${work.cause}")
                        if (work.requiredParts.isNotBlank()) Text("Запчасти: ${work.requiredParts}")
                        Text(listOf(work.location, work.performerName).filter { it.isNotBlank() }.joinToString(" · "))
                    } }
                }
                item { Text("Фото, видео и исправление истории доступны в веб-версии.", style = MaterialTheme.typography.bodySmall) }
            }
            if (editing) RepairWorkDialog(defect, acting, onDismiss = { editing = false }) { input ->
                vm.act { repo.enqueue(row, defect, input); editing = false; vm.sync() }
            }
            if (claim) AlertDialog(onDismissRequest = { claim = false }, title = { Text("Взять заявку в работу?") },
                text = { Text("Команда сохранится на телефоне. Назначение подтвердит сервер при синхронизации.") },
                confirmButton = { TextButton(enabled = !acting, onClick = { vm.act { repo.enqueue(row, defect); claim = false; vm.sync() } }) { Text("Подтвердить") } },
                dismissButton = { TextButton(onClick = { claim = false }) { Text("Отмена") } })
        } else LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(10.dp)) {
            item { Text("Ремонт", style = MaterialTheme.typography.headlineMedium) }
            item { Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                listOf("open" to "Открытые", "done" to "Завершённые", "queue" to "Очередь (${queue.size})").forEach { (key, label) ->
                    FilterChip(selected = mode == key, onClick = { mode = key }, label = { Text(label) })
                }
            } }
            if (mode == "queue") {
                if (queue.isEmpty()) item { Text("Неотправленных записей ремонта нет") }
                items(queue, key = { it.id }) { operation ->
                    val command = wireJson.decodeFromString<RepairCommand>(operation.payload)
                    Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(operation.title, style = MaterialTheme.typography.titleMedium)
                        SelectionContainer { Text(command.work?.let { "${repairLabel(it.status)}\n${it.description}\nПричина: ${it.cause}\nЗапчасти: ${it.requiredParts}" } ?: "Взять заявку в работу") }
                        Text(operation.message)
                        if (operation.status in listOf("conflict", "rejected")) {
                            Text("Сверьте запись с обновлённым журналом. Скопируйте нужный текст перед удалением и повторным вводом.")
                            TextButton(onClick = { discard = operation }, enabled = !acting && !syncing) { Text("Удалить отклонённую запись") }
                        }
                    } }
                }
            } else {
                item { OutlinedTextField(query, { query = it }, label = { Text("Машина, номер, неисправность") }, modifier = Modifier.fillMaxWidth()) }
                val defects = rows.flatMap { r -> r.journal.defects.map { d -> r to d } }.filter { (r, d) ->
                    (d.status == "repaired") == (mode == "done") &&
                        "${r.journal.vehicle.title} ${r.journal.vehicle.stateNumber} ${d.symptoms} ${d.nodeName}".contains(query, true)
                }.sortedByDescending { it.second.createdAt }
                if (snapshot == null) item { Text("Нажмите синхронизацию для загрузки ремонта. После загрузки журнал доступен без сети.") }
                else if (defects.isEmpty()) item { Text("Неисправности не найдены") }
                items(defects, key = { it.second.id }) { (r, d) ->
                    Card(onClick = { selected = d.id }, modifier = Modifier.fillMaxWidth()) {
                        Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                            Text(r.journal.vehicle.title, style = MaterialTheme.typography.titleMedium)
                            Text(repairLabel(d.status)); Text(d.symptoms)
                            if (queue.any { it.vehicleId == r.journal.vehicle.id }) Text("Есть запись на телефоне", style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
            }
        }
    }
    discard?.let { operation -> AlertDialog(onDismissRequest = { discard = null }, title = { Text("Удалить локальную запись?") },
        text = { Text("Удаляется только отклонённая команда на телефоне. Записи сервера останутся без изменений.") },
        confirmButton = { TextButton(onClick = { vm.act { repo.discard(operation.id); discard = null } }) { Text("Удалить") } },
        dismissButton = { TextButton(onClick = { discard = null }) { Text("Отмена") } }) }
}

@Composable private fun RepairWorkDialog(defect: RepairDefect, busy: Boolean, onDismiss: () -> Unit, onSave: (RepairWorkInput) -> Unit) {
    var description by rememberSaveable(defect.id) { mutableStateOf("") }
    var cause by rememberSaveable(defect.id) { mutableStateOf(defect.failureReason) }
    var parts by rememberSaveable(defect.id) { mutableStateOf("") }
    var status by rememberSaveable(defect.id) { mutableStateOf("faulty") }
    var location by rememberSaveable(defect.id) { mutableStateOf("") }
    var state by rememberSaveable(defect.id) { mutableStateOf(defect.operationalState) }
    var time by rememberSaveable(defect.id) { mutableStateOf(LocalDateTime.now(ZoneId.of("Asia/Sakhalin")).format(DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm"))) }
    var downtime by rememberSaveable(defect.id) { mutableStateOf(defect.downtimeStartedAt?.let { runCatching { OffsetDateTime.parse(it).atZoneSameInstant(ZoneId.of("Asia/Sakhalin")).format(DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm")) }.getOrNull() } ?: time) }
    var error by remember { mutableStateOf<String?>(null) }
    AlertDialog(onDismissRequest = { if (!busy) onDismiss() }, title = { Text("Результат ремонта") }, text = {
        LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp)) {
            item { Text(defect.symptoms) }
            item { OutlinedTextField(description, { description = it }, label = { Text("Выполненные работы") }, minLines = 3, enabled = !busy) }
            item { OutlinedTextField(cause, { cause = it }, label = { Text("Причина отказа") }, enabled = !busy) }
            item { Text("Место работ"); Row { listOf("КАРЬЕР", "РММ").forEach { place -> FilterChip(location == place, { location = place }, label = { Text(place) }, enabled = !busy) } } }
            item { Text("Результат") }
            items(listOf("repaired", "faulty", "awaiting_parts")) { value ->
                FilterChip(status == value, { status = value; if (value == "repaired") state = "operating" }, label = { Text(repairLabel(value)) }, enabled = !busy)
            }
            if (status == "awaiting_parts") item { OutlinedTextField(parts, { parts = it }, label = { Text("Необходимые запчасти") }, enabled = !busy) }
            if (status != "repaired") item {
                FilterChip(state == "operating", { state = "operating" }, label = { Text("Эксплуатация продолжается") }, enabled = !busy)
                FilterChip(state == "stopped", { state = "stopped" }, label = { Text("Машина остановлена") }, enabled = !busy)
            }
            item { OutlinedTextField(time, { time = it }, label = { Text("Время ремонта · Сахалин") }, supportingText = { Text("ГГГГ-ММ-ДДTЧЧ:ММ") }, enabled = !busy) }
            if (state == "stopped") item { OutlinedTextField(downtime, { downtime = it }, label = { Text("Начало простоя · Сахалин") }, supportingText = { Text("ГГГГ-ММ-ДДTЧЧ:ММ") }, enabled = !busy) }
            item { Text("Сохранится на телефоне. При синхронизации сервер проверит права и изменения журнала.") }
            error?.let { item { Text(it, color = MaterialTheme.colorScheme.error) } }
        }
    }, confirmButton = { TextButton(enabled = !busy, onClick = {
        try {
            fun instant(text: String) = LocalDateTime.parse(text).atZone(ZoneId.of("Asia/Sakhalin")).toOffsetDateTime().toString()
            val work = RepairWorkInput(defect.id, cause.trim(), description.trim(), status, parts.trim(), instant(time), state,
                if (state == "stopped") instant(downtime) else null, location)
            validateRepair(work, defect.id); onSave(work)
        } catch (e: Exception) { error = if (e is java.time.format.DateTimeParseException) "Проверьте дату и время: ГГГГ-ММ-ДДTЧЧ:ММ" else e.message }
    }) { Text("Сохранить на телефоне") } }, dismissButton = { TextButton(onClick = onDismiss, enabled = !busy) { Text("Отмена") } })
}
