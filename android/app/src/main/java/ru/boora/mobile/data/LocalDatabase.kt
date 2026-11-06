package ru.boora.mobile.data

import androidx.room.*
import kotlinx.coroutines.flow.Flow
import androidx.room.migration.Migration
import androidx.sqlite.db.SupportSQLiteDatabase

@Entity(tableName = "snapshots")
data class SnapshotEntity(@PrimaryKey val owner: String, val json: String, val fetchedAt: Long)

@Entity(tableName = "outbox", indices = [Index(value = ["owner", "vehicleId"], unique = true)])
data class PendingOperation(
    @PrimaryKey val id: String, val owner: String, val vehicleId: String,
    val vehicleTitle: String, val payload: String, val createdAt: Long,
    val status: String = "pending", val message: String = "Ожидает отправки",
)

@Dao interface LocalDao {
    @Query("SELECT * FROM snapshots WHERE owner = :owner") fun observeSnapshot(owner: String): Flow<SnapshotEntity?>
    @Query("SELECT * FROM snapshots WHERE owner = :owner") suspend fun snapshot(owner: String): SnapshotEntity?
    @Upsert suspend fun saveSnapshot(snapshot: SnapshotEntity)
    @Query("SELECT * FROM outbox WHERE owner = :owner ORDER BY createdAt") fun observeQueue(owner: String): Flow<List<PendingOperation>>
    @Query("SELECT * FROM outbox WHERE owner = :owner ORDER BY createdAt") suspend fun queue(owner: String): List<PendingOperation>
    @Insert suspend fun enqueue(operation: PendingOperation)
    @Query("UPDATE outbox SET status = :status, message = :message WHERE id = :id")
    suspend fun mark(id: String, status: String, message: String)
    @Query("DELETE FROM outbox WHERE id = :id AND owner = :owner") suspend fun remove(id: String, owner: String)
}

@Database(entities = [SnapshotEntity::class, PendingOperation::class, RepairCache::class, RepairPending::class], version = 2, exportSchema = false)
abstract class LocalDatabase : RoomDatabase() {
    abstract fun dao(): LocalDao
    abstract fun repairDao(): RepairDao
    companion object {
        val MIGRATION_1_2 = object : Migration(1, 2) {
            override fun migrate(db: SupportSQLiteDatabase) {
                db.execSQL("CREATE TABLE IF NOT EXISTS repair_snapshots (owner TEXT NOT NULL PRIMARY KEY, json TEXT NOT NULL, fetchedAt INTEGER NOT NULL)")
                db.execSQL("CREATE TABLE IF NOT EXISTS repair_outbox (id TEXT NOT NULL PRIMARY KEY, owner TEXT NOT NULL, vehicleId TEXT NOT NULL, title TEXT NOT NULL, payload TEXT NOT NULL, createdAt INTEGER NOT NULL, status TEXT NOT NULL, message TEXT NOT NULL)")
                db.execSQL("CREATE UNIQUE INDEX IF NOT EXISTS index_repair_outbox_owner_vehicleId ON repair_outbox (owner, vehicleId)")
            }
        }
    }
}
