using Microsoft.EntityFrameworkCore;

namespace HVO.Edge.Outbox;

public sealed record EdgeOutboxSchemaValidationResult(
    bool IsCompatible,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public void ThrowIfIncompatible()
    {
        if (IsCompatible)
            return;

        throw new InvalidOperationException(
            "The local edge outbox SQLite schema is incompatible with the shared outbox contract. "
            + string.Join(" ", Errors));
    }
}

public static class EdgeOutboxSchemaValidator
{
    private static readonly HashSet<string> CurrentColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "SourceId",
        "DeviceId",
        "PayloadType",
        "PayloadVersion",
        "RecordedAtUtc",
        "Payload",
        "Status",
        "AttemptCount",
        "LastAttemptedAtUtc",
        "SentAtUtc",
        "NextRetryAtUtc",
        "LastError",
        "FailureKind",
        "CreatedAtUtc",
    };

    private static readonly HashSet<string> RequiredColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id",
        "SourceId",
        "PayloadType",
        "PayloadVersion",
        "RecordedAtUtc",
        "Payload",
        "Status",
        "AttemptCount",
        "NextRetryAtUtc",
        "FailureKind",
        "CreatedAtUtc",
    };

    public static async Task<EdgeOutboxSchemaValidationResult> ValidateAsync(
        EdgeOutboxDbContext db,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            return new EdgeOutboxSchemaValidationResult(true, [], ["Schema validation is only implemented for SQLite outbox databases."]);

        var columns = await ReadColumnsAsync(db, ct).ConfigureAwait(false);
        var errors = new List<string>();
        var warnings = new List<string>();

        if (columns.Count == 0)
        {
            errors.Add("OutboxRecords table is missing.");
            return new EdgeOutboxSchemaValidationResult(false, errors, warnings);
        }

        foreach (var requiredColumn in RequiredColumns)
        {
            if (!columns.ContainsKey(requiredColumn))
                errors.Add($"OutboxRecords.{requiredColumn} column is missing.");
        }

        foreach (var column in columns.Values)
        {
            if (CurrentColumns.Contains(column.Name))
                continue;

            warnings.Add($"OutboxRecords.{column.Name} is a legacy column not used by the shared outbox contract.");
            if (column.NotNull && string.IsNullOrWhiteSpace(column.DefaultValue) && !column.IsPrimaryKey)
            {
                errors.Add(
                    $"OutboxRecords.{column.Name} is a legacy NOT NULL column with no default; "
                    + "new shared outbox inserts may fail until the DB is migrated, archived, or re-baselined.");
            }
        }

        return new EdgeOutboxSchemaValidationResult(errors.Count == 0, errors, warnings);
    }

    public static async Task ValidateOrThrowAsync(EdgeOutboxDbContext db, CancellationToken ct = default)
    {
        var result = await ValidateAsync(db, ct).ConfigureAwait(false);
        result.ThrowIfIncompatible();
    }

    private static async Task<Dictionary<string, SqliteColumnInfo>> ReadColumnsAsync(
        EdgeOutboxDbContext db,
        CancellationToken ct)
    {
        var columns = new Dictionary<string, SqliteColumnInfo>(StringComparer.OrdinalIgnoreCase);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA table_info('OutboxRecords')";
        if (command.Connection!.State != System.Data.ConnectionState.Open)
            await command.Connection.OpenAsync(ct).ConfigureAwait(false);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = reader.GetString(1);
            columns[name] = new SqliteColumnInfo(
                name,
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5) == 1);
        }

        return columns;
    }

    private sealed record SqliteColumnInfo(string Name, bool NotNull, string? DefaultValue, bool IsPrimaryKey);
}
