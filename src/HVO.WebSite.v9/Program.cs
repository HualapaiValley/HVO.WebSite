using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using HVO.DataModels.Extensions;
using OpenTelemetry.Resources;
using HVO.Enterprise.Telemetry;
using HVO.Enterprise.Telemetry.HealthChecks;
using HVO.Enterprise.Telemetry.Http;
using HVO.Enterprise.Telemetry.OpenTelemetry;
using HVO.Enterprise.Telemetry.Serilog;
using HVO.Edge.Hosting.Logging;
using OpenTelemetry.Trace;
using Microsoft.OpenApi;
using Microsoft.AspNetCore.Components.Web;
using HVO.WebSite.v9.Middleware;
using HVO.WebSite.v9.Services;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using HVO.DataModels.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Net.Http;
using System.Net;
using Azure.Identity;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MudBlazor.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;
using HVO.WebSite.v9.Telemetry;
using HVO.WebSite.v9.Configuration;
using OpenTelemetry.Metrics;
namespace HVO.WebSite.v9
{
    /// <summary>
    /// Entry point class for the HVO Website Playground application
    /// </summary>
    public class Program
    {
        /// <summary>
        /// Application entry point
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Load secrets from Azure Key Vault when a URI is configured.
            // DefaultAzureCredential resolves credentials in order:
            //   1. Environment vars (AZURE_CLIENT_ID / SECRET / TENANT_ID) — devcontainer SP
            //   2. Azure CLI (az login) — local bare-metal dev
            //   3. Managed Identity — when deployed to Azure
            var hostDatabaseConnection = builder.Configuration.GetConnectionString(HostConfigurationPrecedence.DatabaseConnectionName);
            var kvUri = builder.Configuration["KeyVault:Uri"];
            if (!string.IsNullOrWhiteSpace(kvUri))
            {
                builder.Configuration.AddAzureKeyVault(
                    new Uri(kvUri),
                    new DefaultAzureCredential());

                // Host-local settings must win over legacy cloud secrets. In particular,
                // self-hosted deployments provide the authoritative local SQL connection.
                HostConfigurationPrecedence.Restore(builder.Configuration, hostDatabaseConnection);
            }

            // Emit one sanitized stdout stream; Docker bounds it and Promtail ships it once.
            var consoleLogger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(new JsonFormatter(renderMessage: true))
                .CreateLogger();
            var loggerConfig = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("HVO.WebSite.v9", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithTelemetry()
                .Enrich.With(new SensitivePropertyRedactionEnricher())
                .WriteTo.Sink(new SanitizingLogEventSink(consoleLogger));

            if (builder.Environment.IsDevelopment())
                loggerConfig
                    .MinimumLevel.Override("HVO.WebSite.v9", LogEventLevel.Debug)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
                    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Information);
            Log.Logger = loggerConfig.CreateLogger();
            // ClearProviders removes default console/debug providers. Additional OTel
            // providers are added below.
            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger, dispose: true);

            ConfigureServices(builder.Services, builder.Configuration);

            var app = builder.Build();
            Configure(app);

            app.Run();
        }

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // ============================================================================
            // ASP.NET CORE BUILT-IN FUNCTIONALITY GUIDELINES
            // ============================================================================
            // Always use built-in ASP.NET Core endpoints and middleware instead of 
            // creating custom controllers for standard functionality:
            //
            // ✅ Health Checks: Use MapHealthChecks() - NOT custom HealthController
            // ✅ OpenAPI/Swagger: Use AddOpenApi() - NOT custom documentation endpoints  
            // ✅ Exception Handling: Use AddExceptionHandler() - NOT custom error controllers
            // ✅ Problem Details: Use AddProblemDetails() - NOT custom error responses
            // ✅ Static Files: Use UseStaticFiles() - NOT custom file serving controllers
            // ✅ CORS: Use AddCors() - NOT custom CORS controllers
            // ✅ Authentication: Use AddAuthentication() - NOT custom auth controllers
            // ✅ Authorization: Use AddAuthorization() - NOT custom authz controllers
            // ✅ Rate Limiting: Use AddRateLimiter() - NOT custom rate limiting controllers
            // ✅ Caching: Use AddResponseCaching() - NOT custom cache controllers
            // ✅ API Versioning: Use AddApiVersioning() - NOT custom version controllers
            // ============================================================================

            // Add Razor Components for Blazor Server
            // AddCascadingAuthenticationState registers authentication state as a cascading value
            // so AuthorizeView and AuthorizeRouteView can access it in both SSR and interactive modes
            services.AddRazorComponents()
                .AddInteractiveServerComponents();
            services.AddMudServices();
            services.AddCascadingAuthenticationState();

            ConfigureDataProtection(services, configuration);
            ConfigureForwardedHeaders(services, configuration);

            // Add Microsoft Entra ID authentication (OpenID Connect + cookie auth)
            // Client secret is loaded from Key Vault at startup (AzureAd--ClientSecret)
            services.AddMicrosoftIdentityWebAppAuthentication(configuration, "AzureAd");

            // Add authorization services with API key scope policies and Entra app role policies
            services.AddAuthorization(options =>
            {
                options.AddPolicy("WeatherIngest", p => p.RequireClaim("scope", ApiScopes.WeatherIngest));
                options.AddPolicy("ImageIngest", p => p.RequireClaim("scope", ApiScopes.ImageIngest));
                options.AddPolicy("PowerIngest", p => p.RequireClaim("scope", ApiScopes.PowerIngest));
                options.AddPolicy("BmsIngest", p => p.RequireClaim("scope", ApiScopes.BmsIngest));
                options.AddPolicy("PowerRead", p => p.RequireClaim("scope", ApiScopes.PowerRead, ApiScopes.ApiRead));
                options.AddPolicy("PowerStatusView", p => p.RequireAssertion(context =>
                    context.User.IsInRole(AppRoles.User)
                    || context.User.IsInRole(AppRoles.Admin)
                    || context.User.HasClaim("scope", ApiScopes.PowerRead)
                    || context.User.HasClaim("scope", ApiScopes.ApiRead)));
                options.AddPolicy("WeatherRead", p => p.RequireClaim("scope", ApiScopes.WeatherRead, ApiScopes.ApiRead));
                options.AddPolicy("AdminOnly", p => p.RequireRole(AppRoles.Admin));
                options.AddPolicy("UserOrAdmin", p => p.RequireRole(AppRoles.User, AppRoles.Admin));
            });

            // API key cache — short-lived to avoid DB hit on every request
            services.AddMemoryCache();
            services.AddOptions<HVO.WebSite.v9.Configuration.PowerCompositionOptions>()
                .Bind(configuration.GetSection(HVO.WebSite.v9.Configuration.PowerCompositionOptions.SectionName))
                .ValidateOnStart();
            services.AddSingleton<IValidateOptions<HVO.WebSite.v9.Configuration.PowerCompositionOptions>, HVO.WebSite.v9.Configuration.PowerCompositionOptionsValidator>();
            services.AddSingleton<TimeProvider>(TimeProvider.System);
            services.AddScoped<IPowerReadingIngestService, PowerReadingIngestService>();
            services.AddScoped<IBmsIngestService, BmsIngestService>();
            services.AddScoped<IPowerSystemSnapshotProvider, PowerSystemSnapshotProvider>();
            services.AddScoped<IPowerInventoryConfigurationProvider, PowerInventoryConfigurationProvider>();

            // Add MVC controllers (includes Microsoft Identity UI controllers for sign-in/sign-out)
            services.AddControllersWithViews()
                .AddMicrosoftIdentityUI()
                .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
            services.AddControllers()
                .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            // Add exception handling middleware
            // NOTE: Use built-in exception handling instead of custom error controllers
            // This provides consistent error responses and integrates with Problem Details
            services.AddExceptionHandler<HvoServiceExceptionHandler>();

            // Configure Problem Details for consistent error responses
            services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            {
                // Add common properties to all problem details
                context.ProblemDetails.Instance = $"{context.HttpContext.Request.Method} {context.HttpContext.Request.Path}";
                context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                context.ProblemDetails.Extensions["timestamp"] = DateTime.UtcNow;

                // Add request information for debugging
                var activity = context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity;
                context.ProblemDetails.Extensions.TryAdd("activityId", activity?.Id);

                if (context.HttpContext.Request.Headers.ContainsKey("User-Agent"))
                {
                    context.ProblemDetails.Extensions["userAgent"] = context.HttpContext.Request.Headers["User-Agent"].ToString();
                }
            });

            // Add health checks
            // NOTE: Use built-in ASP.NET Core health check endpoints instead of creating custom controllers
            // The MapHealthChecks middleware below provides all necessary endpoints:
            // - /health (detailed health information)
            // - /health/ready (readiness probes for load balancers)  
            // - /health/live (liveness probes for container orchestration)
            // Do NOT create duplicate HealthController - use the built-in functionality
            services.AddHealthChecks()
                .AddDbContextCheck<HvoDbContext>("database-legacy", tags: new[] { "database", "ef" })
                .AddDbContextCheck<HvoV9DbContext>("database-v9", tags: new[] { "database", "ef" });

            // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
            // NOTE: Use built-in OpenAPI/Swagger functionality instead of custom documentation endpoints
            // This provides automatic API documentation generation from controller attributes
            services.AddOpenApi("v1", options =>
            {
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info = new OpenApiInfo
                    {
                        Title = "HVO Weather API",
                        Version = "v1.0",
                        Description = "Hualapai Valley Observatory Weather API for accessing current conditions and daily highs/lows",
                        Contact = new OpenApiContact
                        {
                            Name = "HVO Development Team",
                            Email = "admin@hualapai-valley-observatory.com"
                        }
                    };
                    return Task.CompletedTask;
                });
            });

            // Enable endpoints API explorer for OpenAPI
            services.AddEndpointsApiExplorer();

            // Add API versioning
            services.AddApiVersioning(opt =>
            {
                opt.DefaultApiVersion = new ApiVersion(1, 0);
                opt.AssumeDefaultVersionWhenUnspecified = true;
                opt.ReportApiVersions = true;
                opt.ApiVersionReader = new UrlSegmentApiVersionReader();
            }).AddMvc();

            // ── Telemetry ──────────────────────────────────────────────────────────────────────────────
            services.AddTelemetry(configuration.GetSection("Telemetry"));
            // HVO library sets the OTLP endpoint programmatically, which disables AppendSignalPathToEndpoint
            // in OTel SDK 1.10+, causing exports to POST to the root URL (404). Disable HVO's built-in
            // exporters and use native SDK exporters with no configure callback — the SDK reads
            // OTEL_EXPORTER_OTLP_ENDPOINT and OTEL_EXPORTER_OTLP_PROTOCOL from environment and appends
            // the correct signal paths (/v1/traces and /v1/metrics).
            services.AddOpenTelemetryExport(options =>
            {
                options.EnableTraceExport = false;
                options.EnableMetricsExport = false;
                options.EnableLogExport = false;
                options.EnableStandardMeters = true;
                options.AdditionalMeterNames.Add(PowerIngestTelemetry.MeterName);
                options.AdditionalActivitySources.Add("HVO.Edge");
            });
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")))
            {
                services.AddOpenTelemetry()
                    .WithTracing(tb => tb.AddOtlpExporter())
                    .WithMetrics(mb => mb.AddOtlpExporter());
            }
            services.AddTelemetryStatistics();
            services.AddTelemetryHealthCheck();

            // Add HVO Data Services with Entity Framework
            services.AddHvoDataServices(configuration);

            // Add application services
            services.AddScoped<HVO.WebSite.v9.Services.IWeatherService, HVO.WebSite.v9.Services.WeatherService>();
            services.AddScoped<HVO.WebSite.v9.Services.ISiteConfigurationService, HVO.WebSite.v9.Services.SiteConfigurationService>();
            services.AddSingleton<PowerIngestTelemetry>();

            // Configure HttpClient for Blazor Server components
            // In Development (or when configured), trust the local dev certificate to avoid SSL issues over port forwarding
            services.AddHttpClient("LocalApi", client =>
            {
                client.BaseAddress = new Uri("http://localhost:5136");
            })
            .AddHttpMessageHandler(sp => new TelemetryHttpMessageHandler(
                new HttpInstrumentationOptions { CaptureRequestHeaders = false, CaptureResponseHeaders = false },
                sp.GetService<ILogger<TelemetryHttpMessageHandler>>()))
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var env = sp.GetRequiredService<IHostEnvironment>();
                var trustDevCerts = env.IsDevelopment()
                    && config.GetValue("TrustDevCertificates", true);

                var handler = new HttpClientHandler();
                if (trustDevCerts)
                {
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                return handler;
            });

            services.AddHttpContextAccessor();

            // Runs DB migrations and seeds system API keys on startup
            services.AddHostedService<Services.ApiKeySeedService>();
        }

        private static void ConfigureDataProtection(IServiceCollection services, IConfiguration configuration)
        {
            var dataProtection = services.AddDataProtection()
                .SetApplicationName(configuration["DataProtection:ApplicationName"] ?? "HVO.WebSite.v9");

            var blobUri = configuration["DataProtection:BlobUri"];
            var keyIdentifier = configuration["DataProtection:KeyIdentifier"];

            if (string.IsNullOrWhiteSpace(blobUri) || string.IsNullOrWhiteSpace(keyIdentifier))
            {
                return;
            }

            var credential = new DefaultAzureCredential();
            dataProtection
                .PersistKeysToAzureBlobStorage(new Uri(blobUri), credential)
                .ProtectKeysWithAzureKeyVault(new Uri(keyIdentifier), credential);
        }

        private static void ConfigureForwardedHeaders(IServiceCollection services, IConfiguration configuration)
        {
            var forwardedHeadersEnabled = configuration.GetValue("ForwardedHeaders:Enabled",
                configuration.GetValue("ASPNETCORE_FORWARDEDHEADERS_ENABLED", false));

            if (!forwardedHeadersEnabled)
            {
                return;
            }

            services.PostConfigure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Trust only the nearest proxy hop and let the host-level
                // ASPNETCORE_FORWARDEDHEADERS_ENABLED switch control whether
                // proxy forwarding is enabled for this deployment.
                options.ForwardLimit = 1;

                var configuredKnownNetworks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Exists();
                var configuredKnownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Exists();
                if (!configuredKnownNetworks && !configuredKnownProxies)
                {
                    options.KnownIPNetworks.Clear();
#pragma warning disable ASPDEPR005
                    options.KnownNetworks.Clear();
#pragma warning restore ASPDEPR005
                    options.KnownProxies.Clear();
                }
            });
        }

        private static void Configure(WebApplication app)
        {
            // ============================================================================
            // MIDDLEWARE PIPELINE CONFIGURATION
            // ============================================================================
            // Use built-in ASP.NET Core middleware in the correct order:
            // 1. Exception handling (UseExceptionHandler)
            // 2. Status code pages (UseStatusCodePages) 
            // 3. HTTPS redirection (UseHttpsRedirection)
            // 4. Authentication (UseAuthentication)
            // 5. Authorization (UseAuthorization)
            // 6. Endpoint mapping (MapControllers, MapHealthChecks, etc.)
            // ============================================================================

            var forwardedHeadersEnabled = app.Configuration.GetValue("ForwardedHeaders:Enabled",
                app.Configuration.GetValue("ASPNETCORE_FORWARDEDHEADERS_ENABLED", false));
            if (forwardedHeadersEnabled)
            {
                // Respect proxy-provided scheme/remote IP only when the deployment
                // explicitly opts into forwarded header processing.
                app.UseForwardedHeaders();
                app.Use((context, next) =>
                {
                    if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var protoValues)
                        && string.Equals(protoValues.FirstOrDefault(), "https", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Request.Scheme = Uri.UriSchemeHttps;
                    }

                    return next(context);
                });
            }

            // Add exception handling middleware
            app.UseExceptionHandler();

            // Add Problem Details middleware for consistent error responses
            app.UseStatusCodePages();

            // Built-in OpenAPI endpoint - provides automatic API documentation
            // Available at: /openapi/v1.json
            app.MapOpenApi();

            if (app.Environment.IsDevelopment())
            {
                // Built-in interactive API documentation - provides Scalar UI
                // Available at: /scalar/v1 (interactive API explorer)
                app.MapScalarApiReference();
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            // Enable HTTPS redirection based on configuration (disable in Development by default)
            var enableHttpsRedirect = app.Configuration.GetValue("EnableHttpsRedirect", !app.Environment.IsDevelopment());
            if (enableHttpsRedirect)
            {
                app.UseHttpsRedirection();
            }
            app.UseRouting();
            app.UseAuthentication();
            app.UseMiddleware<ApiKeyAuthMiddleware>();
            app.UseAuthorization();
            app.UseAntiforgery();
            app.MapStaticAssets();

            // Add health check endpoints
            // IMPORTANT: These are the RECOMMENDED ASP.NET Core health check endpoints
            // Do NOT duplicate these with custom controllers - use these built-in endpoints:

            // Minimal public aggregate health endpoint. Detailed health data is restricted to
            // Development, or can be explicitly enabled in trusted networks with
            // HealthChecks:ExposeDetailed=true.
            var exposeDetailedHealth = app.Environment.IsDevelopment()
                || app.Configuration.GetValue("HealthChecks:ExposeDetailed", false);
            app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";
                    if (!exposeDetailedHealth)
                    {
                        await context.Response.WriteAsJsonAsync(new
                        {
                            status = report.Status.ToString(),
                            timestamp = DateTime.UtcNow
                        });
                        return;
                    }

                    var response = new
                    {
                        status = report.Status.ToString(),
                        checks = report.Entries.Select(x => new
                        {
                            name = x.Key,
                            status = x.Value.Status.ToString(),
                            description = x.Value.Description,
                            data = x.Value.Data,
                            duration = x.Value.Duration.ToString(),
                            exception = app.Environment.IsDevelopment() ? x.Value.Exception?.Message : null,
                            tags = x.Value.Tags
                        }),
                        totalDuration = report.TotalDuration.ToString(),
                        timestamp = DateTime.UtcNow
                    };
                    await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
                }
            });

            // Readiness probe endpoint for load balancers and orchestration
            // Use this for: Kubernetes readiness probes, load balancer health checks
            // Only checks database-tagged components to determine if service is ready to serve traffic
            app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("database")
            });

            // Liveness probe endpoint for container orchestration
            // Use this for: Kubernetes liveness probes, container restart decisions
            // Always returns healthy if the application is running (no specific checks)
            app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                Predicate = _ => false // Always returns healthy for liveness
            });

            // Map MVC controllers with default route
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // Map API controllers
            app.MapControllers();

            // Map Razor components for Blazor Server
            app.MapRazorComponents<Components.App>()
                .AddInteractiveServerRenderMode();
        }
    }
}
