using Firefox_Bookmarks_Cleanup.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Firefox_Bookmarks_Cleanup.Extensions
{
    internal static class ServiceCollectionExtension
    {
        internal static IServiceCollection ConfigureLogAndServices(this IServiceCollection services)
        {
            var environment =
                Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile(
                    $"appsettings.{environment}.json",
                    optional: true,
                    reloadOnChange: true
                )
                .Build();

            var browserDb = configuration.GetConnectionString("BrowserDb");
            var db = configuration.GetConnectionString("Db");
            if (string.IsNullOrWhiteSpace(browserDb))
            {
                throw new InvalidOperationException(
                    "Database connection string 'BrowserDb' is missing in configuration."
                );
            }

            if (string.IsNullOrWhiteSpace(db))
            {
                configuration["ConnectionStrings:Db"] =
                    $"Data Source={Directory.GetCurrentDirectory()}\\firefox-bookmarks.sqlite";
            }

            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();

            services
                .AddSingleton<IConfiguration>(configuration)
                .AddLogging(configure => configure.AddSerilog())
                .AddScoped<IDatabaseService, DatabaseService>()
                .AddScoped<IBookmarkService, BookmarkService>()
                .AddScoped<BookmarkCleanupRunner>();

            return services;
        }
    }
}
