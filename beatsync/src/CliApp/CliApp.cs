using beatsync.CliCommands;
using Microsoft.Extensions.DependencyInjection;
using ZLogger;
using ZLogger.Providers;
using CliFx;

namespace beatsync;

public static class CliApp
{
    public static void Run(IServiceCollection services, string[]? args = null)
    {
        // Register commands (e.g. services.AddTransient<MyCommand>())
        services.AddTransient<SyncLibrary>();

        var serviceProvider = services.BuildServiceProvider();

        var app = new CommandLineApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .UseTypeInstantiator(serviceProvider.GetRequiredService)
            .Build();

        if (args is not null)
        {
            app.RunAsync(args).GetAwaiter().GetResult();
        }
        else
        {
            app.RunAsync().GetAwaiter().GetResult();
        }
    }
}