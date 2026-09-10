using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public static class GuiApp
{
    private const int MaxDirectoryLength = 1024;
    
    private static bool _isBrowsingSource;
    private static bool _isBrowsingTarget;
    
    private static AppState _appState = new();
    
    private static Vector4 _goodColor = new Vector4(0.4f, 0.8f, 0.4f, 1f);
    private static Vector4 _warningColor = new Vector4(0.9f, 0.7f, 0.2f, 1f);
    private static Vector4 _errorColor = new Vector4(0.9f, 0.4f, 0.4f, 1f);

    public static void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.HighDpiWindow | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(850, 480, "Beat Sync");

        rlImGui.Setup(true, false);	// sets up ImGui with ether a dark or light default theme

        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(0, 0, 0, 1));

            rlImGui.Begin();			// starts the ImGui content mode. Make all ImGui calls after this

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

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.WorkPos);
            ImGui.SetNextWindowSize(viewport.WorkSize);

            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0.0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0.0f);

            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar
                | ImGuiWindowFlags.NoResize
                | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoCollapse
                | ImGuiWindowFlags.NoBringToFrontOnFocus
                | ImGuiWindowFlags.NoNavFocus;

            bool isWindowOpen = ImGui.Begin("beatsync", windowFlags);
            ImGui.PopStyleVar(2);

            if (isWindowOpen)
            {
                ImGui.Text("Source Directory:");
                ImGui.SetNextItemWidth(450);
                ImGui.InputText("##SourcePath", ref _appState.SourcePath, 1024);
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
                        
                        PickDirectoryAsync(_appState.SourcePath, "Select Source Directory", (selected) =>
                        {
                            _appState.SourcePath = selected;
                            _isBrowsingSource = false;
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(_appState.SourcePath))
                {
                    if (Directory.Exists(_appState.SourcePath))
                    {
                        ImGui.TextColored(_goodColor, "✓ Directory exists");
                    }
                    else
                    {
                        ImGui.TextColored(_errorColor, "✗ Directory not found");
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                ImGui.Text("Target Directory:");
                ImGui.SetNextItemWidth(450);
                ImGui.InputText("##TargetPath", ref _appState.TargetPath, MaxDirectoryLength);
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
                        
                        PickDirectoryAsync( _appState.TargetPath, "Select Target Directory", (selected) =>
                        {
                            _appState.TargetPath = selected;
                            _isBrowsingTarget = false;
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(_appState.TargetPath))
                {
                    if (Directory.Exists(_appState.TargetPath))
                    {
                        ImGui.TextColored(_goodColor, "✓ Directory exists");
                    }
                    else
                    {
                        ImGui.TextColored(_warningColor, "! Directory will be created upon sync");
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

    private static void PickDirectoryAsync(string initialPath, string prompt, Action<string?> onComplete)
    {
        Task.Run(() =>
        {
            string? selected = null;
            try
            {
                selected = FolderDialog.PickFolder(prompt, initialPath);
               
            }
            finally
            {
                onComplete?.Invoke(selected);
            }
        });
    }
}