# Notification tests

Run every .NET project and test from the repository root:

```powershell
dotnet build notification-api/NSA.sln --configuration Release
dotnet test notification-api/NSA.sln --configuration Release --no-build
```

Database tests use isolated SQLite databases in memory. Each fixture keeps a
connection open for its lifetime; individual contexts open separate connections
to that database. Disposing the fixture releases the database. Schemas and seed
data are created with `EnsureCreated`, so transactions, foreign keys, unique
indexes, and bulk SQL updates/deletes are exercised without a database server.

Test-only model customization stores `DateTimeOffset` as UTC ticks for sorting
and comparisons and maps SQL Server's `nvarchar(max)` to SQLite `TEXT`.
Production SQL Server mappings and migrations are unchanged. The API fixture
replaces the SQL Server application-lock implementation because SQLite's
serializable write transaction provides database-level exclusion.

SQLite does not verify SQL Server migrations or `sp_getapplock` semantics. The
opt-in `LiveIntegration` test uses the real SQL Server/RabbitMQ stack and runs
when `NSA_LIVE_BASE_URL` points to that running API; otherwise it is skipped.
