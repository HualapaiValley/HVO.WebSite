using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using HVO.Ingest.Contracts;
using HVO.Ingest.Contracts.Weather;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HVO.Ingest.Functions;

public sealed class ProcessWeatherRawV1(
    IConfiguration configuration,
    ILogger<ProcessWeatherRawV1> logger)
{
    private const string TopicName = "hvo-ingest";
    private const string SubscriptionName = "weather-raw-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Function(nameof(ProcessWeatherRawV1))]
    public async Task Run(
        [ServiceBusTrigger(TopicName, SubscriptionName, Connection = "ServiceBusConnection")] string body,
        CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<CanonicalEnvelope<WeatherRawV1>>(body, JsonOptions)
            ?? throw new InvalidOperationException("Service Bus message body was empty or invalid JSON.");

        ValidateEnvelope(envelope);
        ValidatePayload(envelope.Payload);

        var payload = envelope.Payload;
        var recordedAtUtc = ResolveRecordedAtUtc(payload, envelope);

        try
        {
            var inserted = await InsertWeatherRawAsync(payload, recordedAtUtc, ct);
            if (!inserted)
            {
                logger.LogDebug(
                    "Duplicate weather raw record from station {StationId} at {RecordedAt} skipped",
                    payload.StationId,
                    recordedAtUtc);
                return;
            }
        }
        catch (SqlException ex) when (IsUniqueConstraintViolation(ex))
        {
            logger.LogDebug(
                ex,
                "Duplicate weather raw record from station {StationId} at {RecordedAt} skipped after race",
                payload.StationId,
                recordedAtUtc);
            return;
        }

        logger.LogInformation(
            "Persisted weather raw record from station {StationId} at {RecordedAt}",
            payload.StationId,
            recordedAtUtc);
    }

    private static void ValidateEnvelope(CanonicalEnvelope<WeatherRawV1> envelope)
    {
        if (envelope.Schema != HvoSchemas.WeatherRawV1)
        {
            throw new ValidationException(
                $"Expected schema '{HvoSchemas.WeatherRawV1}' but received '{envelope.Schema}'.");
        }

        if (envelope.ObservedAtUtc == default)
        {
            throw new ValidationException("Envelope observedAtUtc is required.");
        }
    }

    private static void ValidatePayload(WeatherRawV1 payload)
    {
        var results = new List<ValidationResult>();
        if (!Validator.TryValidateObject(payload, new ValidationContext(payload), results, validateAllProperties: true))
        {
            throw new ValidationException(string.Join("; ", results.Select(r => r.ErrorMessage)));
        }
    }

    private static DateTime ResolveRecordedAtUtc(
        WeatherRawV1 payload,
        CanonicalEnvelope<WeatherRawV1> envelope)
    {
        var recordedAt = payload.RecordedAt ?? envelope.ObservedAtUtc;
        return recordedAt.UtcDateTime;
    }

    private async Task<bool> InsertWeatherRawAsync(WeatherRawV1 payload, DateTime recordedAtUtc, CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("HualapaiValleyObservatory")
            ?? configuration["SqlConnectionString"]
            ?? throw new InvalidOperationException(
                "Configure ConnectionStrings:HualapaiValleyObservatory or SqlConnectionString for ingest persistence.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO [v9].[WeatherRaw]
            (
                [RecordedAt],
                [StationId],
                [TemperatureF],
                [HumidityPercent],
                [DewPointF],
                [BarometricPressureInHg],
                [WindSpeedMph],
                [WindGustMph],
                [WindDirectionDegrees],
                [RainfallInches],
                [SolarRadiationWm2],
                [UvIndex]
            )
            SELECT
                @RecordedAt,
                @StationId,
                @TemperatureF,
                @HumidityPercent,
                @DewPointF,
                @BarometricPressureInHg,
                @WindSpeedMph,
                @WindGustMph,
                @WindDirectionDegrees,
                @RainfallInches,
                @SolarRadiationWm2,
                @UvIndex
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM [v9].[WeatherRaw] WITH (UPDLOCK, HOLDLOCK)
                WHERE [StationId] = @StationId AND [RecordedAt] = @RecordedAt
            );

            SELECT CAST(@@ROWCOUNT AS int);
            """;
        command.CommandType = CommandType.Text;
        command.Parameters.Add("@RecordedAt", SqlDbType.DateTime2).Value = recordedAtUtc;
        command.Parameters.Add("@StationId", SqlDbType.NVarChar, 64).Value = payload.StationId;
        AddNullableDouble(command, "@TemperatureF", payload.TemperatureF);
        AddNullableDouble(command, "@HumidityPercent", payload.HumidityPercent);
        AddNullableDouble(command, "@DewPointF", payload.DewPointF);
        AddNullableDouble(command, "@BarometricPressureInHg", payload.BarometricPressureInHg);
        AddNullableDouble(command, "@WindSpeedMph", payload.WindSpeedMph);
        AddNullableDouble(command, "@WindGustMph", payload.WindGustMph ?? payload.WindGust10MinMph);
        AddNullableInt(command, "@WindDirectionDegrees", payload.WindDirectionDegrees);
        AddNullableDouble(command, "@RainfallInches", payload.RainfallInches);
        AddNullableDouble(command, "@SolarRadiationWm2", payload.SolarRadiationWm2);
        AddNullableDouble(command, "@UvIndex", payload.UvIndex);

        var result = await command.ExecuteScalarAsync(ct);
        return result is int inserted && inserted == 1;
    }

    private static void AddNullableDouble(SqlCommand command, string name, double? value) =>
        command.Parameters.Add(name, SqlDbType.Float).Value = value.HasValue ? value.Value : DBNull.Value;

    private static void AddNullableInt(SqlCommand command, string name, int? value) =>
        command.Parameters.Add(name, SqlDbType.Int).Value = value.HasValue ? value.Value : DBNull.Value;

    private static bool IsUniqueConstraintViolation(SqlException ex) =>
        ex.Number == 2601 || ex.Number == 2627;
}
