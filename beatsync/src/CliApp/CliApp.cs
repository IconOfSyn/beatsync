using Microsoft.Extensions.DependencyInjection;
using ZLogger;
using ZLogger.Providers;
using CliFx;

namespace beatsync;

public static class CliApp
{
    public static void Run()
    {
        var cliBuilder = new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .UseTypeActivator(commandTypes =>
            {
                var services = new ServiceCollection();

// Logging system
                services.AddLogging(loggingBuilder =>
                {
                    // output to  yyyy-MM_*.log, roll by 1MB or changed date
                    loggingBuilder.AddZLoggerRollingFile(options =>
                    {
                        options.RollingInterval = RollingInterval.Infinite;
                        options.FilePathSelector = (dt, index) => $"{dt:yyyy-MM}_{index}.log";
                        options.RollingSizeKB = 1024 * 1024;
                        options.UsePlainTextFormatter(formatter =>
                        {
                            formatter.SetPrefixFormatter($"{0:utc-longdate}|{1:short}|",
                                (in MessageTemplate template, in LogInfo info) =>
                                    template.Format(info.Timestamp, info.LogLevel));
                        });
                    });
                });

// Register services

// Register commands
                foreach (var commandType in commandTypes)
                    services.AddTransient(commandType);

                return services.BuildServiceProvider();
            })
            .Build();

        cliBuilder.Run();
    }
}