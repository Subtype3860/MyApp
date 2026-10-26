package ru.boora.mobile

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.BackHandler
import androidx.activity.compose.setContent
import androidx.activity.enableEdgeToEdge
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.compose.ui.res.stringResource
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.launch
import ru.boora.mobile.data.*
import java.time.*
import java.time.format.DateTimeFormatter

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        enableEdgeToEdge()
        setContent {
            MaterialTheme(colorScheme = lightColorScheme(primary = Color(0xFF176B62), secondary = Color(0xFF506660),
                background = Color(0xFFF4F7F6), surface = Color(0xFFF4F7F6))) { BooraScreen() }
        }
    }
}

@OptIn(ExperimentalCoroutinesApi::class)
class MainViewModel(app: android.app.Application) : AndroidViewModel(app) {
    private val application = app as BooraApplication
    val repo = application.repository
    val session = repo.sessions.current
    val snapshot = session.flatMapLatest { s -> s?.let { repo.dao.observeSnapshot(it.owner) } ?: flowOf(null) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), null)
    val queue = session.flatMapLatest { s -> s?.let { repo.dao.observeQueue(it.owner) } ?: flowOf(emptyList()) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())
    val repairQueue = session.flatMapLatest { s -> s?.let { repo.repairs?.dao?.observeQueue(it.owner) } ?: flowOf(emptyList()) }
        .stateIn(viewModelScope, SharingStarted.WhileSubscribed(5000), emptyList())
    val error = MutableStateFlow<String?>(null)
    val acting = MutableStateFlow(false)
    fun act(block: suspend () -> Unit) { viewModelScope.launch {
        acting.value = true
        try { block() } catch (e: CancellationException) { throw e }
        catch (e: Exception) { error.value = e.message ?: "Не удалось выполнить действие" }
        finally { acting.value = false }
    } }
    fun login(name: String, password: String, done: () -> Unit) = act {
        repo.login(name, password); application.scheduleSync(); done()
    }
    fun sync() { application.scheduleSync(); act { repo.sync() } }
    fun complete(row: MaintenanceRow, date: String, text: String, runInId: String?, done: () -> Unit) = act {
        repo.enqueue(row, date, text, runInId); application.scheduleSync(); done()
    }
    init { application.scheduleSync() }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable fun BooraScreen(vm: MainViewModel = viewModel()) {
    val session by vm.session.collectAsStateWithLifecycle()
    val cached by vm.snapshot.collectAsStateWithLifecycle()
    val storedQueue by vm.queue.collectAsStateWithLifecycle()
    val repairQueue by vm.repairQueue.collectAsStateWithLifecycle()
    val queue = storedQueue.filter { it.owner == session?.owner }
    val status by vm.repo.message.collectAsStateWithLifecycle()
    val syncing by vm.repo.busy.collectAsStateWithLifecycle()
    val acting by vm.acting.collectAsStateWithLifecycle()
    val authenticationRequired by vm.repo.authenticationRequired.collectAsStateWithLifecycle()
    val error by vm.error.collectAsStateWithLifecycle()
    var tab by rememberSaveable { mutableIntStateOf(0) }
    var selectedId by rememberSaveable { mutableStateOf<String?>(null) }
    var loginAgain by rememberSaveable { mutableStateOf(false) }
    var query by rememberSaveable { mutableStateOf("") }
    var completionVehicle by rememberSaveable { mutableStateOf<String?>(null) }
    val snapshot = remember(cached, session?.owner) { cached?.takeIf { it.owner == session?.owner }?.let { wireJson.decodeFromString<MaintenanceSnapshot>(it.json) } }
    val selected = snapshot?.rows?.find { it.vehicle.id == selectedId }
    BackHandler(selectedId != null || loginAgain) { selectedId = null; loginAgain = false }
    Scaffold(
        topBar = { TopAppBar(title = { Column {
            Text(if (selected != null && !authenticationRequired) selected.vehicle.title else stringResource(R.string.app_name), style = MaterialTheme.typography.titleLarge)
            Text("Техника и обслуживание", style = MaterialTheme.typography.labelMedium)
        } }, navigationIcon = {
            if (selectedId != null && !authenticationRequired) IconButton(onClick = { selectedId = null }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Назад") }
        }, actions = {
            if (session != null && !authenticationRequired) IconButton(onClick = vm::sync, enabled = !syncing && !acting) { Icon(Icons.Default.Sync, "Синхронизировать") }
        }) },
        bottomBar = {
            if (session != null && !loginAgain && !authenticationRequired) NavigationBar {
                listOf("Техника", "Ремонт", "Очередь ТО (${queue.size})", "Профиль").forEachIndexed { index, title ->
                    NavigationBarItem(selected = tab == index, onClick = { tab = index; selectedId = null },
                        icon = { Icon(listOf(Icons.Default.DirectionsCar, Icons.Default.Build, Icons.Default.CloudUpload, Icons.Default.Person)[index], null) }, label = { Text(title) })
                }
            }
        },
    ) { padding ->
        Column(Modifier.padding(padding).fillMaxSize()) {
            if (syncing || acting) LinearProgressIndicator(Modifier.fillMaxWidth())
            if (session == null || loginAgain || authenticationRequired) {
                if (authenticationRequired) Text("Сессия истекла. Войдите снова. Неотправленные записи сохранены на телефоне.", Modifier.padding(16.dp))
                LoginForm(acting, session?.displayName.orEmpty(), onLogin = { name, password ->
                    vm.login(name, password) { loginAgain = false }
                })
                if (session != null && !authenticationRequired) TextButton(onClick = { loginAgain = false }) { Text("Вернуться к локальным данным") }
            } else {
                if (tab != 1) Surface(color = MaterialTheme.colorScheme.secondaryContainer) {
                    Column(Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 10.dp)) {
                        Text(status, style = MaterialTheme.typography.bodySmall)
                        Text(cached?.let { "Копия данных: " + Instant.ofEpochMilli(it.fetchedAt).atZone(ZoneId.of("Asia/Sakhalin")).format(DateTimeFormatter.ofPattern("dd.MM HH:mm")) + " · Сахалин" }
                            ?: "Локальных данных пока нет", style = MaterialTheme.typography.labelSmall)
                    }
                }
                when (tab) {
                    0 -> if (selected != null) {
                        VehicleDetail(selected, snapshot?.canManage == true, queue.any { it.vehicleId == selected.vehicle.id },
                            onComplete = { completionVehicle = selected.vehicle.id })
                    } else LazyColumn(Modifier.fillMaxSize(), contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
                        item { OutlinedTextField(query, { query = it }, Modifier.fillMaxWidth(), label = { Text("Модель, номер или группа") }, singleLine = true) }
                        val rows = snapshot?.rows.orEmpty().filter { !it.vehicle.isArchived &&
                            (it.vehicle.title + it.vehicle.groupName + it.vehicle.stateNumber).contains(query, ignoreCase = true) }
                        item {
                            Text("Парк техники · ${rows.size}", style = MaterialTheme.typography.titleMedium)
                            if (snapshot == null) Text("Подключитесь к интернету и нажмите синхронизацию. После загрузки карточки будут доступны без сети.")
                            else if (rows.isEmpty()) Text("Техника не найдена")
                        }
                        items(rows, key = { it.vehicle.id }) { row ->
                            Card(onClick = { selectedId = row.vehicle.id }, modifier = Modifier.fillMaxWidth()) {
                                Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                                    Text(row.vehicle.title, style = MaterialTheme.typography.titleMedium)
                                    Text(row.vehicle.groupName, style = MaterialTheme.typography.bodySmall)
                                    val due = row.due.minByOrNull { it.remaining }
                                    Text(due?.let { "${it.name}: ${formatHours(it.remaining)} м/ч до ТО" } ?: "График ТО не настроен или нет показаний",
                                        color = if ((due?.remaining ?: 0.0) < 0) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary)
                                    if (queue.any { it.vehicleId == row.vehicle.id }) Text("Есть локальная запись", style = MaterialTheme.typography.labelMedium)
                                }
                            }
                        }
                    }
                    1 -> RepairScreen(vm)
                    2 -> QueueScreen(queue, onDiscard = { id -> vm.act { vm.repo.discard(id) } })
                    3 -> Column(Modifier.padding(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
                        Text(session!!.displayName, style = MaterialTheme.typography.headlineSmall)
                        Text("Сервер: boora.ru\nВерсия: ${BuildConfig.VERSION_NAME}")
                        Text("Без сети доступны загруженные карточки и запись выполненного ТО. Сервер подтвердит запись после проверки прав, версии графика и показаний.")
                        Button(onClick = { loginAgain = true }, enabled = !acting) { Text("Повторить вход") }
                        OutlinedButton(onClick = { vm.act { vm.repo.logout() } }, enabled = !acting && queue.isEmpty() && repairQueue.none { it.owner == session?.owner }) { Text("Выйти") }
                        if (queue.isNotEmpty() || repairQueue.any { it.owner == session?.owner }) Text("Выход доступен после обработки очередей ТО и ремонта, чтобы не потерять ваши записи.")
                    }
                }
            }
        }
    }
    if (error != null) AlertDialog(onDismissRequest = { vm.error.value = null }, title = { Text("Не удалось выполнить действие") },
        text = { Text(error!!) }, confirmButton = { TextButton(onClick = { vm.error.value = null }) { Text("Понятно") } })
    snapshot?.rows?.find { !authenticationRequired && it.vehicle.id == completionVehicle }?.let { row ->
        CompletionDialog(row, acting, onDismiss = { completionVehicle = null }, onSave = { date, text, runIn ->
            vm.complete(row, date, text, runIn) { completionVehicle = null }
        })
    }
}

@Composable private fun LoginForm(busy: Boolean, initialName: String, onLogin: (String, String) -> Unit) {
    var name by rememberSaveable { mutableStateOf(initialName) }
    var password by remember { mutableStateOf("") }
    LazyColumn(contentPadding = PaddingValues(24.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        item { Text("Работа на участке", style = MaterialTheme.typography.headlineMedium) }
        item { Text("Войдите с учётной записью boora.ru. Для первого входа нужен интернет.") }
        item { OutlinedTextField(name, { name = it }, Modifier.fillMaxWidth(), label = { Text("Логин или почта") }, singleLine = true) }
        item { OutlinedTextField(password, { password = it }, Modifier.fillMaxWidth(), label = { Text("Пароль") }, singleLine = true,
            visualTransformation = PasswordVisualTransformation(), keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Password)) }
        item { Button(onClick = { onLogin(name, password); password = "" }, enabled = !busy && name.isNotBlank() && password.isNotEmpty(), modifier = Modifier.fillMaxWidth()) { Text("Войти") } }
    }
}

@Composable private fun VehicleDetail(row: MaintenanceRow, canManage: Boolean, pending: Boolean, onComplete: () -> Unit) {
    LazyColumn(contentPadding = PaddingValues(20.dp), verticalArrangement = Arrangement.spacedBy(16.dp)) {
        item { Text(row.vehicle.groupName, style = MaterialTheme.typography.titleMedium); Text("Госномер: ${row.vehicle.stateNumber.ifBlank { "не указан" }}") }
        item { Text("Ближайшее обслуживание", style = MaterialTheme.typography.headlineSmall) }
        if (row.due.isEmpty()) item { Text("Нет графика или исходных моточасов. Настройте график на сайте и обновите данные.") }
        items(row.due) { due ->
            Card(Modifier.fillMaxWidth()) { Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(due.name, style = MaterialTheme.typography.titleLarge)
                Text("ДВС ${due.engineNumber} · ${formatHours(due.currentHours)} м/ч")
                Text("Осталось ${formatHours(due.remaining)} м/ч", color = if (due.remaining < 0) MaterialTheme.colorScheme.error else MaterialTheme.colorScheme.primary)
                Text("По данным на ${due.readingDate}${if (due.isEstimated) " · расчётные показания" else ""}", style = MaterialTheme.typography.bodySmall)
            } }
        }
        item {
            if (row.incomplete) Text("Недостаточно показаний для полного расчёта", color = MaterialTheme.colorScheme.error)
            Button(onComplete, enabled = canManage && !pending && row.plan != null) { Text(if (pending) "ТО ожидает синхронизации" else "Записать выполненное ТО") }
            if (!canManage) Text("Просмотр доступен; права на регистрацию ТО отсутствуют")
        }
    }
}

@Composable private fun CompletionDialog(row: MaintenanceRow, busy: Boolean, onDismiss: () -> Unit, onSave: (String, String, String?) -> Unit) {
    var date by rememberSaveable(row.vehicle.id) { mutableStateOf(LocalDate.now(ZoneId.of("Asia/Sakhalin")).toString()) }
    var text by rememberSaveable(row.vehicle.id) { mutableStateOf("") }
    var runIn by rememberSaveable(row.vehicle.id) { mutableStateOf<String?>(null) }
    AlertDialog(onDismissRequest = { if (!busy) onDismiss() }, title = { Text("Выполненное ТО") },
        text = { LazyColumn(verticalArrangement = Arrangement.spacedBy(12.dp)) {
            item { Text(row.vehicle.title) }
            item { OutlinedTextField(date, { date = it }, label = { Text("Дата: ГГГГ-ММ-ДД") }, singleLine = true) }
            item { FilterChip(selected = runIn == null, onClick = { runIn = null }, label = { Text("Плановое ТО") }) }
            items(row.due.filter { it.runInId != null }.distinctBy { it.runInId }) { due ->
                FilterChip(selected = runIn == due.runInId, onClick = { runIn = due.runInId }, label = { Text(due.name) })
            }
            item { OutlinedTextField(text, { text = it }, label = { Text("Что выполнено") }, minLines = 3) }
            item { Text("Запись сохранится на телефоне. При отправке сервер определит моточасы на выбранную дату и проверит версию графика.") }
        } }, confirmButton = { TextButton(onClick = { onSave(date, text, runIn) }, enabled = !busy && text.isNotBlank()) { Text("Сохранить на телефоне") } },
        dismissButton = { TextButton(onClick = onDismiss, enabled = !busy) { Text("Отмена") } })
}

@Composable private fun QueueScreen(queue: List<PendingOperation>, onDiscard: (String) -> Unit) {
    var discard by remember { mutableStateOf<PendingOperation?>(null) }
    LazyColumn(contentPadding = PaddingValues(16.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
        item { Text("Синхронизация", style = MaterialTheme.typography.headlineSmall) }
        if (queue.isEmpty()) item { Text("Неотправленных записей нет") }
        items(queue, key = { it.id }) { op -> Card(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                val payload = wireJson.decodeFromString<Completion>(op.payload)
                Text(op.vehicleTitle, style = MaterialTheme.typography.titleMedium)
                Text("${payload.date} · ${payload.description}")
                Text(op.message, color = if (op.status == "pending") MaterialTheme.colorScheme.primary else MaterialTheme.colorScheme.error)
                if (op.status != "pending") {
                    Text("Сравните с обновлённой карточкой. После удаления этой записи можно внести исправленное ТО.")
                    TextButton(onClick = { discard = op }) { Text("Удалить отклонённую запись") }
                }
            }
        } }
    }
    discard?.let { item -> AlertDialog(onDismissRequest = { discard = null }, title = { Text("Удалить локальную запись?") },
        text = { Text("Сервер отклонил эту операцию. Её описание будет удалено с телефона; данные сервера не изменятся.") },
        confirmButton = { TextButton(onClick = { onDiscard(item.id); discard = null }) { Text("Удалить") } },
        dismissButton = { TextButton(onClick = { discard = null }) { Text("Отмена") } }) }
}

private fun formatHours(value: Double) = String.format(java.util.Locale.forLanguageTag("ru"), "%.1f", value)
