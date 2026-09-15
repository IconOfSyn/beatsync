using beatsync;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Providers;

static class Program
{
    // STAThread is required if you deploy using NativeAOT on Windows - See https://github.com/raylib-cs/raylib-cs/issues/301
    [STAThread]
    public static void Main(string[] args)
    {
        bool isGui = Array.Exists(args, a => string.Equals(a, "--gui", StringComparison.OrdinalIgnoreCase));

        var services = CreateServiceCollection();
        
        if (isGui)
        {
            GuiApp.Run(services, args);
        }
        else
        {
            CliApp.Run(services, args);
        }
    }

    private static IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();
        
        // Logging system
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders()
                // Add to output to console
                .AddZLoggerConsole()
                // output to  yyyy-MM_*.log, roll by 1MB or changed date
                .AddZLoggerRollingFile(options =>
                {
                    options.RollingInterval = RollingInterval.Infinite;
                    options.FilePathSelector = (dt, index) =>
                    {
                        string logDir = LogFileUtils.GetLogDirectory();
                        return Path.Combine(logDir, $"worklog-{dt:yyyy-MM-dd}_{index}.log");
                    };
                    options.RollingSizeKB = 1024 * 1024;
                    options.UsePlainTextFormatter(formatter =>
                    {
                        formatter.SetPrefixFormatter($"{0:utc-longdate}|{1:short}|",
                            (in MessageTemplate template, in LogInfo info) =>
                                template.Format(info.Timestamp, info.LogLevel));
                    });
                });
        });

        return services;
    }
}