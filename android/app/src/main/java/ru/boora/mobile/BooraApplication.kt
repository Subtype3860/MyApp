package ru.boora.mobile

import android.app.Application
import android.content.Context
import androidx.room.Room
import androidx.work.*
import ru.boora.mobile.data.*
import java.util.concurrent.TimeUnit

class BooraApplication : Application() {
    val repository by lazy {
        val database = Room.databaseBuilder(this, LocalDatabase::class.java, "boora.db").addMigrations(LocalDatabase.MIGRATION_1_2).build()
        Repository(database.dao(), SessionStore(this), BooraApi(), database.repairDao())
    }
    override fun onCreate() {
        super.onCreate()
        val constraints = Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build()
        WorkManager.getInstance(this).enqueueUniquePeriodicWork("boora-periodic", ExistingPeriodicWorkPolicy.KEEP,
            PeriodicWorkRequestBuilder<SyncWorker>(15, TimeUnit.MINUTES).setConstraints(constraints).build())
    }
    fun scheduleSync() {
        WorkManager.getInstance(this).enqueueUniqueWork("boora-sync", ExistingWorkPolicy.APPEND_OR_REPLACE,
            OneTimeWorkRequestBuilder<SyncWorker>()
                .setConstraints(Constraints.Builder().setRequiredNetworkType(NetworkType.CONNECTED).build())
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL, 30, TimeUnit.SECONDS).build())
    }
}
class SyncWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {
    override suspend fun doWork(): Result =
        if ((applicationContext as BooraApplication).repository.sync()) Result.success() else Result.retry()
}
