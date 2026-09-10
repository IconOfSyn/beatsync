using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public static class GuiApp
{
    // Constants
    private const int MaxDirectoryLength = 1024;
    private static readonly Vector4 _goodColor = new Vector4(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 _warningColor = new Vector4(0.9f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 _errorColor = new Vector4(0.9f, 0.4f, 0.4f, 1f);

    // App state
    private static AppState _appState = new();
    
    // Transient State
    private static bool _isBrowsingSource;
    private static bool _isBrowsingTarget;
    private static List<SyncJob>? _syncDiffList;
    private static GuiStateType _guiStateType = GuiStateType.Idle;
    private static CancellationTokenSource? _syncCancelTokenSource;
    private static SyncProgressReport _lastSyncProgressReport;
    private static bool _isDryRun;
    
    public static void Run(bool isDryRun)
    {
        _isDryRun = isDryRun;
        
        Raylib.SetConfigFlags(ConfigFlags.HighDpiWindow | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(850, 480, "Beat Sync");

        rlImGui.Setup(true, false); // sets up ImGui with ether a dark or light default theme

        var saveFile = Path.Combine(GetOrCreateWritableDirectory(), "SaveState.bsyn");

        _appState = LoadAppState(saveFile);

        CalculateDiffAsync(_appState, (diffList)=> _syncDiffList = diffList);
        
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
                            
                            CalculateDiffAsync(_appState, (diffList)=> _syncDiffList = diffList);
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
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (_guiStateType == GuiStateType.Syncing)
                {
                    if (ImGui.Button("Cancel"))
                    {
                        _syncCancelTokenSource?.Cancel();
                    }

                    ImGui.Text($"{_lastSyncProgressReport.CompletedCount}/{_lastSyncProgressReport.TotalCount} | %{_lastSyncProgressReport.Percent}");
                    ImGui.ProgressBar(_lastSyncProgressReport.Fraction, new Vector2(ImGui.GetWindowViewport().WorkSize.X, 30f));
                    ImGui.Text($"{_lastSyncProgressReport.CurrentPath}");
                }
                else
                {
                    if (_appState.HasValidPaths())
                    {
                        if (ImGui.Button("Calculate Diff"))
                        {
                            CalculateDiffAsync(_appState, (diffList)=> _syncDiffList = diffList);
                        }
                    }

                    if (_syncDiffList is { Count: > 0 })
                    {
                        ImGui.SameLine();
                        if (ImGui.Button($"Sync {_syncDiffList.Count} tracks"))
                        {
                            BeginSync(_appState, (results) =>
                            {
                                Console.WriteLine($"Completed Sync: {results}");
                            });
                        }
                    }
                }
            }

            ImGui.End();

            rlImGui.End();			// ends the ImGui content mode. Make all ImGui calls before this
            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();		// cleans up ImGui
        Raylib.CloseWindow();
        
        SaveAppState(saveFile, _appState);
    }

    private static void CalculateDiffAsync(AppState appState, Action<List<SyncJob>> onComplete)
    {
        _guiStateType = GuiStateType.CalculatingDiff;
        
        Task.Run(async () =>
        {
            var diffList = await SyncCore.BuildSyncList(appState, default);
            
            _guiStateType = GuiStateType.Idle;
            onComplete?.Invoke(diffList);
        });
    }
    
    private static void BeginSync(AppState appState, Action<SyncResult> onComplete)
    {
        _guiStateType = GuiStateType.Syncing;
        _syncCancelTokenSource = new CancellationTokenSource();

        _lastSyncProgressReport = default;

        var progress = new Progress<SyncProgressReport>(report => _lastSyncProgressReport = report);
        
        Task.Run(async () =>
        {
            var result = await SyncCore.SyncMusicAsync(appState, _isDryRun, progress, _syncCancelTokenSource.Token);
            
            _guiStateType = GuiStateType.Idle;
            onComplete?.Invoke(result);
        });
    }

    private static void SaveAppState(string saveFile, AppState appState)
    {
        var appStateBlob = AppState.Serialize(appState);
        File.WriteAllBytes(saveFile, appStateBlob);
    }
    
    private static AppState LoadAppState(string loadFile)
    {
        if (!File.Exists(loadFile))
            return new AppState();
        
        
        var appStateBlob = File.ReadAllBytes(loadFile);
        var appState = AppState.Deserialize(appStateBlob);

        // if loaded app state is invalid, create a new one
        if (appState.Version <= 0 || appState.TargetPath == null || appState.SourcePath == null)
        {
            Console.WriteLine("Loaded app state is malformed, reverting to default");
            appState = new();
        }
        
        return appState;
    }
    
    private static string GetOrCreateWritableDirectory()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(basePath, "EldritchEngineering", "BeatSync");
        Directory.CreateDirectory(appFolder);

        return appFolder;
    }

    private static void PickDirectoryAsync(string initialPath, string prompt, Action<string> onComplete)
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

    public enum GuiStateType : byte
    {
        Idle,
        CalculatingDiff,
        Syncing
    }
}