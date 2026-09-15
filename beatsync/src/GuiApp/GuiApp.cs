using System.Numerics;
using ImGuiNET;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public static class GuiApp
{
    public static void Run(IServiceCollection services, string[]? args = null)
    {
        services.AddSingleton<GuiAppRunner>();
        
        var serviceProvider = services.BuildServiceProvider();
        var guiAppRunner = serviceProvider.GetService<GuiAppRunner>();
        
        bool isDryRun = Array.Exists(args, a => string.Equals(a, "--dry", StringComparison.OrdinalIgnoreCase));
        guiAppRunner.Run(isDryRun);
    }
}