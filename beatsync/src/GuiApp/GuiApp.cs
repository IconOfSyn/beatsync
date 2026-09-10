using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public static class GuiApp
{
    private static string _sourcePath = "";
    private static string _targetPath = "";
    private static bool _isBrowsingSource;
    private static bool _isBrowsingTarget;

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
                ImGui.Text("Source Directory:");
                ImGui.SetNextItemWidth(450);
                ImGui.InputText("##SourcePath", ref _sourcePath, 1024);
                ImGui.SameLine();
                if (_isBrowsingSource)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Browsing...##Source");
                    ImGui.EndDisabled();
                }
                else
                {
                    if (ImGui.Button("Browse...##Source"))
                    {
                        _isBrowsingSource = true;
                        string current = _sourcePath;
                        Task.Run(() =>
                        {
                            try
                            {
                                string? selected = FolderDialog.PickFolder("Select Source Directory", current);
                                if (!string.IsNullOrWhiteSpace(selected))
                                {
                                    _sourcePath = selected;
                                }
                            }
                            finally
                            {
                                _isBrowsingSource = false;
                            }
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(_sourcePath))
                {
                    if (Directory.Exists(_sourcePath))
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.8f, 0.4f, 1f), "✓ Directory exists");
                    }
                    else
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.9f, 0.4f, 0.4f, 1f), "✗ Directory not found");
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Target Directory:");
                ImGui.SetNextItemWidth(450);
                ImGui.InputText("##TargetPath", ref _targetPath, 1024);
                ImGui.SameLine();
                if (_isBrowsingTarget)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("Browsing...##Target");
                    ImGui.EndDisabled();
                }
                else
                {
                    if (ImGui.Button("Browse...##Target"))
                    {
                        _isBrowsingTarget = true;
                        string current = _targetPath;
                        Task.Run(() =>
                        {
                            try
                            {
                                string? selected = FolderDialog.PickFolder("Select Target Directory", current);
                                if (!string.IsNullOrWhiteSpace(selected))
                                {
                                    _targetPath = selected;
                                }
                            }
                            finally
                            {
                                _isBrowsingTarget = false;
                            }
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(_targetPath))
                {
                    if (Directory.Exists(_targetPath))
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.4f, 0.8f, 0.4f, 1f), "✓ Directory exists");
                    }
                    else
                    {
                        ImGui.TextColored(new System.Numerics.Vector4(0.9f, 0.7f, 0.2f, 1f), "! Directory will be created upon sync");
                    }
                }
            }

            ImGui.End();

            rlImGui.End();			// ends the ImGui content mode. Make all ImGui calls before this
            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();		// cleans up ImGui
        Raylib.CloseWindow();
    }
}