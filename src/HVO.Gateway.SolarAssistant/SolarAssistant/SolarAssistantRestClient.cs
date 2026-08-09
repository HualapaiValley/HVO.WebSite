using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using HVO.Gateway.SolarAssistant.Configuration;
using Microsoft.Extensions.Options;

namespace HVO.Gateway.SolarAssistant.SolarAssistant;

/// <summary>Read-only REST client for SolarAssistant /api/v1/metrics.</summary>
public sealed class SolarAssistantRestClient : ISolarAssistantClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly SolarAssistantOptions _options;
    private readonly ILogger<SolarAssistantRestClient> _logger;

    public SolarAssistantRestClient(
        IHttpClientFactory httpFactory,
        IOptions<SolarAssistantOptions> options,
        ILogger<SolarAssistantRestClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SolarAssistantMetric>> GetMetricsAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Host))
            return [];

        var client = _httpFactory.CreateClient("SolarAssistantRest");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"http://{_options.Host}:{_options.RestPort}/api/v1/metrics");

        if (!string.IsNullOrWhiteSpace(_options.BearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.BearerToken);
        }
        else if (!string.IsNullOrWhiteSpace(_options.RestPassword))
        {
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.RestUsername}:{_options.RestPassword}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }

        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "SolarAssistant REST returned HTTP {StatusCode} for metrics request",
                (int)response.StatusCode);
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<SolarAssistantMetric>>(cancellationToken: ct) ?? [];
    }

}
