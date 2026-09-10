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

    
    // Command handler
    private static BeatCommandHandler _handler = new();
    
    // Transient State
    private static bool _isBrowsingSource;
    private static bool _isBrowsingTarget;
    private static List<SyncJob>? _syncDiffList;
    private static GuiStateType _guiStateType = GuiStateType.Idle;
    private static SyncProgressReport _lastSyncProgressReport;
    private static bool _isDryRun;
    private static string? _lastErrorMessage;
    private static string? _lastInfoMessage;
    private static string _tempSourcePath = "";
    private static string _tempTargetPath = "";
    
    public static void Run(bool isDryRun)
    {
        _isDryRun = isDryRun;
        
        Raylib.SetConfigFlags(ConfigFlags.HighDpiWindow | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(850, 480, "Beat Sync");

        rlImGui.Setup(true, false); // sets up ImGui with ether a dark or light default theme

        var saveFile = Path.Combine(GetOrCreateWritableDirectory(), "SaveState.bsyn");

        _handler.AppState = LoadAppState(saveFile);

        _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff });

        _tempSourcePath = _handler.AppState.SourcePath;
        _tempTargetPath = _handler.AppState.TargetPath;
        
        while (!Raylib.WindowShouldClose())
        {
            _handler.ProcessCommands();
            DrainResults();
            
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(0, 0, 0, 1));

            rlImGui.Begin();			// starts the ImGui content mode. Make all ImGui calls after this

            // if (ImGui.BeginMainMenuBar()) {
            //     if (ImGui.BeginMenu("File")) {
            //         ImGui.MenuItem("New", "Ctrl+N");
            //         ImGui.MenuItem("Open", "Ctrl+O");
            //         ImGui.EndMenu();
            //     }
            //     if (ImGui.BeginMenu("Edit")) {
            //         ImGui.MenuItem("Open", "Ctrl+O");
            //         ImGui.EndMenu();
            //     }
            //     ImGui.EndMainMenuBar();
            // }

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
                if (!string.IsNullOrWhiteSpace(_lastErrorMessage))
                {
                    ImGui.TextColored(_errorColor, $"{_lastErrorMessage}");
                
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                }
                
                if (!string.IsNullOrWhiteSpace(_lastInfoMessage))
                {
                    ImGui.TextWrapped($"{_lastInfoMessage}");
                
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();
                }
                
                ImGui.Text("Source Directory:");
                ImGui.SetNextItemWidth(450);
                
                if (ImGui.InputText("##SourcePath", ref _tempSourcePath, MaxDirectoryLength))
                {
                    _handler.Submit(new BeatCommand
                    {
                        Type = CommandType.MountSourceDirectory,
                        Path = _tempSourcePath
                    });
                }
                
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
                        
                        PickDirectoryAsync(_handler.AppState.SourcePath, "Select Source Directory", (selected) =>
                        {
                            _isBrowsingSource = false;
                            
                            _handler.Submit(new BeatCommand
                            {
                                Type = CommandType.MountSourceDirectory,
                                Path = selected,
                            });
                            
                            _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff });
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(_handler.AppState.SourcePath))
                {
                    if (Directory.Exists(_handler.AppState.SourcePath))
                    {
                        ImGui.TextColored(_goodColor, "* Directory exists");
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
                if (ImGui.InputText("##TargetPath", ref _tempTargetPath, MaxDirectoryLength))
                {
                    _handler.Submit(new BeatCommand
                    {
                        Type = CommandType.MountTargetDirectory,
                        Path = _tempTargetPath
                    });
                }
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
                        
                        PickDirectoryAsync(_handler.AppState.TargetPath, "Select Target Directory", (selected) =>
                        {
                            _isBrowsingTarget = false;
                            
                            _handler.Submit(new BeatCommand
                            {
                                Type = CommandType.MountTargetDirectory,
                                Path = selected,
                            });
                                
                            _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff });
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(_handler.AppState.TargetPath))
                {
                    if (Directory.Exists(_handler.AppState.TargetPath))
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
                        _handler.Submit(new BeatCommand { Type = CommandType.CancelSync });
                    }

                    ImGui.Text($"{_lastSyncProgressReport.CompletedCount}/{_lastSyncProgressReport.TotalCount} | %{_lastSyncProgressReport.Percent}");
                    ImGui.ProgressBar(_lastSyncProgressReport.Fraction, new Vector2(ImGui.GetWindowViewport().WorkSize.X, 30f));
                    ImGui.Text($"{_lastSyncProgressReport.CurrentPath}");
                }
                else
                {
                    if (_handler.AppState.HasValidPaths())
                    {
                        if (ImGui.Button("Calculate Diff"))
                        {
                            _guiStateType = GuiStateType.CalculatingDiff;
                            _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff });
                        }
                    }

                    if (_syncDiffList is { Count: > 0 })
                    {
                        ImGui.SameLine();
                        if (ImGui.Button($"Sync {_syncDiffList.Count} tracks"))
                        {
                            _guiStateType = GuiStateType.Syncing;
                            _handler.Submit(new BeatCommand
                            {
                                Type = CommandType.SyncLibrary,
                                IsDryRun = _isDryRun,
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
        
        SaveAppState(saveFile, _handler.AppState);
    }

    private static void DrainResults()
    {
        while (_handler.TryDequeueResult(out var result))
        {
            switch (result.CommandType)
            {
                case CommandType.CalculateDiff:
                    _guiStateType = GuiStateType.Idle;
                    _syncDiffList = result.DiffList;
                    _lastInfoMessage = $"Sync Track Count: {result.DiffList?.Count}";
                    _lastErrorMessage = null;
                    break;
                    
                case CommandType.SyncLibrary:
                    _guiStateType = GuiStateType.Idle;
                    _lastInfoMessage = $"Completed Sync: {result.SyncResult}";
                    Console.WriteLine(_lastInfoMessage);
                    _lastErrorMessage = null;
                    break;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                _lastErrorMessage = result.ErrorMessage;
                Console.WriteLine(_lastErrorMessage);
            }
        }
        
        // Read progress from handler (written by background sync task)
        if (_guiStateType == GuiStateType.Syncing)
        {
            _lastSyncProgressReport = _handler.LatestProgress;
        }
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

    public enum GuiStateType : byte
    {
        Idle,
        CalculatingDiff,
        Syncing
    }
}