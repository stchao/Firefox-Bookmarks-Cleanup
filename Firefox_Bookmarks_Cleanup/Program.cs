using Firefox_Bookmarks_Cleanup.Extensions;
using Firefox_Bookmarks_Cleanup.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Firefox_Bookmarks_Cleanup
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var serviceProvider = new ServiceCollection()
                .ConfigureLogAndServices()
                .BuildServiceProvider();

            var logger = serviceProvider.GetService<ILogger<Program>>();
            if (logger is null)
            {
                Console.WriteLine("Failed to get and/or initialize the logger.");
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, args) =>
            {
                cts.Cancel();
                args.Cancel = true;
            };

            var config = serviceProvider.GetService<IConfiguration>();
            bool dryRun = config?.GetValue<bool>("DryRun") ?? false;

            if (args.Any(a => a.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)))
            {
                dryRun = true;
            }

            try
            {
                var runner = serviceProvider.GetRequiredService<BookmarkCleanupRunner>();
                await runner.RunAsync(dryRun, cts.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "An unexpected error occurred when cleaning firefox mobile bookmark(s)."
                );
            }
            finally
            {
                logger.LogInformation("Exiting App.\n");
            }
        }
    }
}
