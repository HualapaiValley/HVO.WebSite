## HVO.Database — Deprecated

This SQL Database project (`.sqlproj`) is **not the canonical source of truth** for the HVO database schema.

### Source of truth

**Entity Framework Core migrations** in `HVO.DataModels` are the single source of truth for all schema changes. This SQL project reflects legacy stored procedures and views from the pre-v9 application and is not synchronised with EF Core migrations.

### Legacy artifacts

The contents of this project (stored procedures, views, legacy table definitions) are retained for historical reference only. New development should use EF Core migrations exclusively.

### Build exclusion

`HVO.Database.sqlproj` is deliberately excluded from `HVO.WebSite.sln`. Normal solution restore, Debug/Release builds, tests and CI do not compile or publish this archived SQL project. Keep it outside the active solution and project-reference graph; retain its files for browsing the old schema and stored procedures.

Tests may read individual SQL reference files directly without building the SQL project.

### Schema management

- EF Core migrations: `src/HVO.DataModels/Migrations/V9/`
- Raw SQLite reference: `src/HVO.Database/v9/Schema.sql` (minimal v9 schema namespace)
- All production schema changes must go through EF Core migrations.
