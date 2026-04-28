using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using HVO.DataModels.Data;

namespace HVO.DataModels.Extensions
{
    /// <summary>
    /// Extension methods for configuring HVO data services
    /// </summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds HVO data services to the dependency injection container
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="configuration">Configuration instance</param>
        /// <param name="connectionStringName">Name of the connection string (default: "HualapaiValleyObservatory")</param>
        /// <returns>Service collection for chaining</returns>
        public static IServiceCollection AddHvoDataServices(
            this IServiceCollection services,
            IConfiguration configuration,
            string connectionStringName = "HualapaiValleyObservatory")
        {
            var connectionString = configuration.GetConnectionString(connectionStringName);
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{connectionStringName}' not found in configuration.");
            }

            // Legacy dbo schema — read-only reference
            services.AddDbContext<HvoDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);

                    sqlOptions.CommandTimeout(60);
                });

                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    options.EnableSensitiveDataLogging();
                }
            });

            // v9 schema — EF Core migrations owned by HvoV9DbContext
            services.AddDbContext<HvoV9DbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);

                    sqlOptions.CommandTimeout(60);
                    sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory", "v9");
                });

                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    options.EnableSensitiveDataLogging();
                }
            });

            return services;
        }

        /// <summary>
        /// Adds HVO data services with a specific connection string
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="connectionString">Database connection string</param>
        /// <returns>Service collection for chaining</returns>
        public static IServiceCollection AddHvoDataServices(
            this IServiceCollection services,
            string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new ArgumentNullException(nameof(connectionString));
            }

            // Add Entity Framework DbContext
            services.AddDbContext<HvoDbContext>(options =>
            {
                options.UseSqlServer(connectionString, sqlOptions =>
                {
                    sqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 3,
                        maxRetryDelay: TimeSpan.FromSeconds(30),
                        errorNumbersToAdd: null);

                    sqlOptions.CommandTimeout(60);
                });

                // Enable sensitive data logging in development
                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
                {
                    options.EnableSensitiveDataLogging();
                }
            });

            return services;
        }

        /// <summary>
        /// Ensures the database is created and migrations are applied
        /// </summary>
        /// <param name="serviceProvider">Service provider</param>
        /// <param name="ensureCreated">Whether to ensure the database is created (default: false)</param>
        /// <returns>Async task</returns>
        public static async Task EnsureHvoDatabaseAsync(
            this IServiceProvider serviceProvider,
            bool ensureCreated = false)
        {
            using var scope = serviceProvider.CreateScope();

            var legacyContext = scope.ServiceProvider.GetRequiredService<HvoDbContext>();
            if (ensureCreated)
                await legacyContext.Database.EnsureCreatedAsync();
            else
                await legacyContext.Database.MigrateAsync();

            var v9Context = scope.ServiceProvider.GetRequiredService<HvoV9DbContext>();
            await v9Context.Database.MigrateAsync();
        }
    }
}
