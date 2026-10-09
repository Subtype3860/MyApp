# CSV stock consistency: transactional limits and migration plan

## Current state

`RequirementJournalRepository.SaveAsync` and `DeleteAsync` write requirement headers/items in PostgreSQL and invoke `edit_csv_tab` to change stock. `CsvFileRepository.ReplaceAsync` invokes PostgreSQL `lo_export` to overwrite a server-side file. These are separate persistence domains.

**Risk:** if `edit_csv_tab` writes an external file, PostgreSQL rollback cannot be assumed to undo its changes. `lo_export` writes a file as a side effect; rollback of its SQL transaction does not revert that file. Do not present a successful SQL rollback as a guarantee of filesystem consistency.

The implementation of the server-side `edit_csv_tab` function is not maintained in this repository and must be inspected in the deployed database before the final migration plan is approved (e.g., inspect its definition and privileges). The test suite uses a database-backed stand-in, **not** the real external CSV function.

## Risk-reduction work adapted to this integration branch

- Validate all requested stock changes against authoritative PostgreSQL stock views before mutating the journal or CSV.
- Aggregate repeated material names per source, reject insufficient quantity rather than clamping negatives to zero.
- Use a transaction-scoped PostgreSQL advisory lock keyed by stock source for save/delete operations originating in this code. This does not serialize direct modifications by CSV uploaders, database administrators or other services.
- Write requirement header and items before invoking the CSV function so relational-constraint failures happen before external writes.
- Use `SELECT ... FOR UPDATE` on a requirement row before restoring stock to prevent simultaneous deletion from restoring the same requirement twice.
- Exercise the logic with a PostgreSQL-backed stand-in function in CI. This is **not evidence of filesystem rollback safety**.

## Still-open failure scenarios

1. A CSV write succeeds, but the following CSV write fails, leaving a partially modified file.
2. A file write succeeds but the PostgreSQL transaction fails during commit or the process crashes.
3. A direct CSV replacement happens outside the advisory lock while a requirement is saved.
4. A cancellation or connection loss occurs after an external file change, before the transaction outcome is known.
5. `lo_export` overwrites a CSV and a later SQL step fails, leaving the old journal state and the new file.

These cases must not be "fixed" by assuming that a rollback or retry makes an external file update idempotent.

## Recommended durable redesign

Treat a normalized PostgreSQL stock ledger / balance table as the source of truth.

1. Add versioned schema migrations for stock balance, stock movements (unique request ID, material key, source, delta and actor), and a transactional outbox.
2. Apply each issue/restore operation in a single database transaction, using conditional SQL updates (`available >= requested`) and unique movement IDs to prevent overdraw or duplicate restoration.
3. Commit an outbox event in the same transaction. An exporter writes CSV into a separate staging file, flushes it, atomically renames it on the **filesystem containing the final CSV** and reports completion. Use checksum and source version for reconciliation.
4. Retries must be idempotent; never apply the stock delta again when replaying an export.
5. Keep downloads/export compatibility during the transition, but stop using CSV as an authoritative editable store.
6. Schedule reconciliation between DB balances and exported CSV snapshots, with alerts for divergence.
7. Roll this out first to a copy of production data with backup, rollback strategy, permissions review and full end-to-end failure injection.

**Do not deploy the stock-ledger migration without first verifying existing CSV schema, `edit_csv_tab` semantics, file permissions and real data.**

## Validation checklist before the PR can be merged

- [ ] Obtain `pg_get_functiondef` for the deployed `edit_csv_tab` and inspect its side effects.
- [ ] Identify all independent CSV writers, direct uploads and `lo_export` call sites.
- [ ] Run end-to-end tests using actual staging CSV files, including crash/cancellation during multiple writes.
- [ ] Confirm that the PostgreSQL advisory locking policy is followed by every participating writer, or remove external write authority.
- [ ] Validate behavior against a restored copy of production schema and data.
- [ ] Add database constraints, migrations and a reconciliation runbook before replacing the current CSV mechanism.
