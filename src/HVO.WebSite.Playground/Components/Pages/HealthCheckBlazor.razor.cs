using Microsoft.AspNetCore.Components;
using System.Net.Http;
using System.Text.Json;

namespace HVO.WebSite.Playground.Components.Pages;

/// <summary>
/// Blazor component for testing health check API endpoints with real-time UI updates
/// </summary>
public partial class HealthCheckBlazor
{
    [Inject] private IHttpClientFactory HttpClientFactory { get; set; } = default!;
    [Inject] private ILogger<HealthCheckBlazor> Logger { get; set; } = default!;

    private bool isLoading = false;
    private bool showResult = false;
    private bool showError = false;
    private string errorMessage = string.Empty;
    private string rawJsonResponse = string.Empty;
    private int statusCode = 0;
    private string statusText = string.Empty;
    private HealthCheckResponse? apiResponse = null;

    protected override async Task OnInitializedAsync()
    {
        // Component initialization
        await base.OnInitializedAsync();
    }

    private async Task CallHealthCheckApi()
    {
        // Reset state
        isLoading = true;
        showResult = false;
        showError = false;
        errorMessage = string.Empty;
        rawJsonResponse = string.Empty;
        apiResponse = null;
        statusCode = 0;
        statusText = string.Empty;
        
        Logger.LogInformation("CallHealthCheckApi method called - starting health check");
        
        // Update UI
        StateHasChanged();

        try
        {
            // Create HttpClient from factory - use the configured LocalApi client
            var httpClient = HttpClientFactory.CreateClient("LocalApi");
            
            Logger.LogInformation("Calling health check endpoint using LocalApi client");
            
            // Call the API using relative path since LocalApi has base address set
            var response = await httpClient.GetAsync("health");
            statusCode = (int)response.StatusCode;
            statusText = response.ReasonPhrase ?? string.Empty;

            Logger.LogInformation("Health check response: {StatusCode} {StatusText}", statusCode, statusText);

            if (response.IsSuccessStatusCode)
            {
                // Read and parse the response
                var jsonContent = await response.Content.ReadAsStringAsync();
                Logger.LogInformation("Raw JSON response: {JsonContent}", jsonContent);
                
                rawJsonResponse = FormatJson(jsonContent);
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                try 
                {
                    apiResponse = JsonSerializer.Deserialize<HealthCheckResponse>(jsonContent, options);
                    Logger.LogInformation("Deserialized response successfully");
                    
                    if (apiResponse != null)
                    {
                        Logger.LogInformation("Response data: Status={Status}, ChecksCount={ChecksCount}", 
                            apiResponse.Status, apiResponse.Checks?.Count ?? 0);
                    }
                    else
                    {
                        Logger.LogWarning("Deserialized response is null");
                    }
                }
                catch (JsonException jsonEx)
                {
                    Logger.LogError(jsonEx, "Failed to deserialize JSON response: {JsonContent}", jsonContent);
                    errorMessage = $"JSON parsing error: {jsonEx.Message}";
                    showError = true;
                    return;
                }
                
                showResult = true;
                
                Logger.LogInformation("Successfully called Health Check API");
            }
            else
            {
                errorMessage = $"HTTP {statusCode}: {statusText}";
                showError = true;
                Logger.LogWarning("Health Check API call failed with status {StatusCode}", statusCode);
            }
        }
        catch (HttpRequestException ex)
        {
            errorMessage = $"Network error: {ex.Message}";
            showError = true;
            Logger.LogError(ex, "Network error calling Health Check API");
        }
        catch (JsonException ex)
        {
            errorMessage = $"JSON parsing error: {ex.Message}";
            showError = true;
            Logger.LogError(ex, "JSON parsing error");
        }
        catch (Exception ex)
        {
            errorMessage = $"Unexpected error: {ex.Message}";
            showError = true;
            Logger.LogError(ex, "Unexpected error calling Health Check API");
        }
        finally
        {
            isLoading = false;
            StateHasChanged();
        }
    }

    private void ClearResult()
    {
        showResult = false;
        showError = false;
        errorMessage = string.Empty;
        rawJsonResponse = string.Empty;
        apiResponse = null;
        statusCode = 0;
        statusText = string.Empty;
        StateHasChanged();
    }

    private string FormatJson(string json)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    // Response model matching the health check API
    public class HealthCheckResponse
    {
        public string Status { get; set; } = string.Empty;
        public List<HealthCheckResult> Checks { get; set; } = new();
        public string TotalDuration { get; set; } = string.Empty;
    }

    public class HealthCheckResult
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string Duration { get; set; } = string.Empty;
    }
}
