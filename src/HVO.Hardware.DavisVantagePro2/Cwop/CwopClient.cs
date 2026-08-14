using System.Net.Sockets;
using System.Text;
using HVO.Hardware.DavisVantagePro2.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Hardware.DavisVantagePro2.Cwop;

internal interface ICwopClient
{
    Task<CwopSendResult> SendAsync(string login, string packet, CancellationToken cancellationToken);
}

internal sealed class CwopClient(IOptions<CwopOptions> options, TimeProvider timeProvider) : ICwopClient
{
    private readonly CwopOptions configuration = options.Value;

    public async Task<CwopSendResult> SendAsync(string login, string packet, CancellationToken cancellationToken)
    {
        using var client = new TcpClient { NoDelay = true };
        try
        {
            using (var timeout = new CancellationTokenSource(configuration.ConnectTimeout, timeProvider))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
                await client.ConnectAsync(configuration.Host, configuration.Port, linked.Token).ConfigureAwait(false);

            await using var stream = client.GetStream();
            var banner = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            if (!banner.StartsWith('#'))
                return new(false, true, CwopOutcome.InvalidResponse);

            await WriteLineAsync(stream, login, cancellationToken).ConfigureAwait(false);
            var loginResponse = await ReadLineAsync(stream, cancellationToken).ConfigureAwait(false);
            var loginOutcome = EvaluateLoginResponse(loginResponse, configuration.StationId, login);
            if (loginOutcome != CwopOutcome.Success)
                return new(false, loginOutcome == CwopOutcome.InvalidResponse, loginOutcome);

            await WriteLineAsync(stream, packet, cancellationToken).ConfigureAwait(false);
            return CwopSendResult.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new(false, true, CwopOutcome.Timeout);
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return new(false, true, CwopOutcome.Transport);
        }
    }

    private async Task<string> ReadLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(configuration.OperationTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var bytes = new List<byte>(128);
        var buffer = new byte[1];
        while (bytes.Count < 511)
        {
            if (await stream.ReadAsync(buffer, linked.Token).ConfigureAwait(false) == 0)
                throw new IOException("APRS-IS disconnected before completing a response line.");
            if (buffer[0] == '\n')
                return Encoding.ASCII.GetString([.. bytes]).TrimEnd('\r');
            bytes.Add(buffer[0]);
        }
        throw new IOException("APRS-IS response exceeded the line limit.");
    }

    private async Task WriteLineAsync(Stream stream, string value, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(configuration.OperationTimeout, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        await stream.WriteAsync(Encoding.ASCII.GetBytes(value + "\r\n"), linked.Token).ConfigureAwait(false);
        await stream.FlushAsync(linked.Token).ConfigureAwait(false);
    }

    private static CwopOutcome EvaluateLoginResponse(string response, string stationId, string login)
    {
        var prefix = $"# logresp {stationId} ";
        if (!response.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return CwopOutcome.InvalidResponse;

        var status = response[prefix.Length..].Split(',', 2)[0].Trim();
        if (status.Equals("verified", StringComparison.OrdinalIgnoreCase))
            return CwopOutcome.Success;
        if (status.Equals("unverified", StringComparison.OrdinalIgnoreCase))
            return UsesReceiveOnlyPasscode(login) ? CwopOutcome.Success : CwopOutcome.LoginRejected;
        if (status.Contains("reject", StringComparison.OrdinalIgnoreCase)
            || status.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || status.Contains("denied", StringComparison.OrdinalIgnoreCase))
            return CwopOutcome.LoginRejected;
        return CwopOutcome.InvalidResponse;
    }

    private static bool UsesReceiveOnlyPasscode(string login)
    {
        var tokens = login.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            if (tokens[index].Equals("pass", StringComparison.OrdinalIgnoreCase))
                return tokens[index + 1] == "-1";
        }
        return false;
    }
}

internal readonly record struct CwopSendResult(bool Succeeded, bool Retryable, CwopOutcome Outcome)
{
    public static CwopSendResult Success { get; } = new(true, false, CwopOutcome.Success);
}

internal enum CwopOutcome
{
    Success,
    NoReading,
    StaleObservation,
    MissingSettings,
    InvalidPosition,
    Timeout,
    Transport,
    InvalidResponse,
    LoginRejected,
}

internal static class CwopOutcomeExtensions
{
    public static string Category(this CwopOutcome outcome) => outcome switch
    {
        CwopOutcome.Success => "success",
        CwopOutcome.NoReading => "no-reading",
        CwopOutcome.StaleObservation => "stale-observation",
        CwopOutcome.MissingSettings => "missing-station-settings",
        CwopOutcome.InvalidPosition => "invalid-position",
        CwopOutcome.Timeout => "timeout",
        CwopOutcome.Transport => "transport",
        CwopOutcome.InvalidResponse => "invalid-response",
        CwopOutcome.LoginRejected => "login-rejected",
        _ => "unknown",
    };
}
