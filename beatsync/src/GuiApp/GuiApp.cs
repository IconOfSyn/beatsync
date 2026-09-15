using Microsoft.Extensions.DependencyInjection;

namespace beatsync;

public static class GuiApp
{
    public static void Run(IServiceCollection services, string[]? args = null)
    {
        services.AddSingleton<GuiAppRunner>();
        services.AddSingleton<BeatCommandHandler>();
        
        var serviceProvider = services.BuildServiceProvider();
        var guiAppRunner = serviceProvider.GetService<GuiAppRunner>();
        
        bool isDryRun = args != null && Array.Exists(args, a => string.Equals(a, "--dry", StringComparison.OrdinalIgnoreCase));
        
        guiAppRunner?.Run(isDryRun);
    }
}