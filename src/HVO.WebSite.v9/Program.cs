using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;
using HVO.DataModels.Extensions;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Components.Web;
using HVO.WebSite.v9.Middleware;
using Microsoft.AspNetCore.Http.Features;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using HVO.DataModels.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

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
            services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Add MVC and API services
            services.AddControllersWithViews()
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
                .AddDbContextCheck<HvoDbContext>("database", tags: new[] { "database", "ef" });

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

            // Add HVO Data Services with Entity Framework
            services.AddHvoDataServices(configuration);

            // Add application services
            services.AddScoped<HVO.WebSite.v9.Services.IWeatherService, HVO.WebSite.v9.Services.WeatherService>();

            // Configure HttpClient for Blazor Server components
            // In Development (or when configured), trust the local dev certificate to avoid SSL issues over port forwarding
            services.AddHttpClient("LocalApi", client =>
            {
                client.BaseAddress = new Uri("http://localhost:5136");
            })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();
                var env = sp.GetRequiredService<IHostEnvironment>();
                var trustDevCerts = config.GetValue("TrustDevCertificates", env.IsDevelopment());

                var handler = new HttpClientHandler();
                if (trustDevCerts)
                {
                    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                return handler;
            });

            services.AddHttpContextAccessor();
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
            app.UseAntiforgery();
            app.MapStaticAssets();

            // Add health check endpoints
            // IMPORTANT: These are the RECOMMENDED ASP.NET Core health check endpoints
            // Do NOT duplicate these with custom controllers - use these built-in endpoints:
            
            // Detailed health endpoint with comprehensive information
            // Use this for: monitoring dashboards, detailed health reporting, troubleshooting
            app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
            {
                ResponseWriter = async (context, report) =>
                {
                    context.Response.ContentType = "application/json";
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
                            exception = x.Value.Exception?.Message,
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
