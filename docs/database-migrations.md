# Database migration policy

Server releases run `dotnet WretchedWhispers.Migrations.dll` before a new API revision receives
traffic. API startup never migrates PostgreSQL, and rollback never reverses a migration.

## Expand, roll out, contract

1. **Expand:** add nullable columns, new tables/indexes, or parallel representations that both the
   current and next application versions can use. Backfill separately when it may take long.
2. **Roll out:** deploy code that can read the old and new schema, switch writes, then verify every
   active revision has stopped depending on the old shape.
3. **Contract later:** remove old columns, tables, constraints, or compatibility code only in a
   later release after the previous Server image passes against the expanded schema.

For example, renaming `Campaign.Name` is two releases: first add `DisplayName`, backfill it, and ship
code that reads either while writing both. A later release stops using `Name`; only a subsequent
migration may drop it.

Every destructive migration PR must state:

- the first deployed version that no longer reads or writes the old schema;
- the earliest later release allowed to remove it;
- the tested application rollback path after the migration has run;
- whether a backfill is required and how it is observed/retried.

The migration executable takes an advisory lock, logs applied/pending IDs, and is safe to rerun.
DDL credentials belong only to the migration job. The API role has connect, schema usage, table DML,
and sequence usage—never schema ownership or create privileges.
