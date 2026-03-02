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
            // Add Entity Framework DbContext
            services.AddDbContext<HvoDbContext>(options =>
            {
                var connectionString = configuration.GetConnectionString(connectionStringName);
                if (string.IsNullOrEmpty(connectionString))
                {
                    throw new InvalidOperationException($"Connection string '{connectionStringName}' not found in configuration.");
                }

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
            var context = scope.ServiceProvider.GetRequiredService<HvoDbContext>();

            if (ensureCreated)
            {
                await context.Database.EnsureCreatedAsync();
            }
            else
            {
                await context.Database.MigrateAsync();
            }
        }
    }
}
