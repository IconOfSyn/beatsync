using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public static class GuiApp
{
    private static bool _nativeLibsInitialized;

    public static void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.HighDpiWindow | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(850, 480, "Beat Sync");

        rlImGui.Setup(true, false);	// sets up ImGui with ether a dark or light default theme

        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(0, 0, 0, 0));

            rlImGui.Begin();			// starts the ImGui content mode. Make all ImGui calls after this

            ImGui.DockSpaceOverViewport(0, ImGui.GetMainViewport(), ImGuiDockNodeFlags.PassthruCentralNode | ImGuiDockNodeFlags.AutoHideTabBar);

            if (ImGui.BeginMainMenuBar()) {
                if (ImGui.BeginMenu("File")) {
                    ImGui.MenuItem("New", "Ctrl+N");
                    ImGui.MenuItem("Open", "Ctrl+O");
                    ImGui.EndMenu();
                }
                if (ImGui.BeginMenu("Edit")) {
                    ImGui.MenuItem("Open", "Ctrl+O");
                    ImGui.EndMenu();
                }
                ImGui.EndMainMenuBar();
            }

            bool laneCheckWindowCreated = ImGui.Begin("beatsync", ImGuiWindowFlags.AlwaysAutoResize);
            if (laneCheckWindowCreated)
            {
            }

            ImGui.End();

            rlImGui.End();			// ends the ImGui content mode. Make all ImGui calls before this
            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();		// cleans up ImGui
        Raylib.CloseWindow();
    }
}