using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public static class GuiApp
{
    private enum GuiStateType : byte
    {
        Idle,
        CalculatingDiff,
        Syncing
    }
    
    // Constants
    private const int MaxDirectoryLength = 1024;
    private static readonly Vector4 _goodColor = new Vector4(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 _warningColor = new Vector4(0.9f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 _errorColor = new Vector4(0.9f, 0.4f, 0.4f, 1f);

    
    private static readonly string[] _spinnerFrames = [".  ", ".. ", "..."];
    private const float AnimationSpeed = 3f;
    
    // Command handler
    private static BeatCommandHandler _handler = new();
    
    // Transient State
    private static bool _isBrowsingSource;
    private static bool _isBrowsingTarget;
    private static DiffResult _cachedDiffResult;
    private static GuiStateType _guiStateType = GuiStateType.Idle;
    private static SyncProgressReport _lastSyncProgressReport;
    private static string? _lastErrorMessage;
    private static string? _lastInfoMessage;
    private static string _tempSourcePath = "";
    private static string _tempTargetPath = "";
    private static int _tempScanStreamCount = 6;
    private static int _tempTransferStreamCount = 4;
    private static bool _diffTimedOut;
    
    public static void Run(bool isDryRun)
    {
        Raylib.SetConfigFlags(ConfigFlags.HighDpiWindow | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(850, 480, "Beat Sync");

        rlImGui.Setup(true, false); // sets up ImGui with ether a dark or light default theme

        var saveFile = Path.Combine(GetOrCreateWritableDirectory(), "SaveState.bsyn");

        _handler.AppState = LoadAppState(saveFile);
        _handler.AppState.IsDryRun = isDryRun;

        if (_handler.AppState.IsValid())
        {
            _guiStateType = GuiStateType.CalculatingDiff;
            _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 100 });
        }

        _tempSourcePath = _handler.AppState.SourcePath;
        _tempTargetPath = _handler.AppState.TargetPath;
        _tempScanStreamCount = _handler.AppState.ScanStreamCount;
        _tempTransferStreamCount = _handler.AppState.TransferStreamCount;
        
        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(0, 0, 0, 1));

            rlImGui.Begin();			// starts the ImGui content mode. Make all ImGui calls after this

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
                
                DrawMainWidgets();
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (_guiStateType == GuiStateType.Syncing)
                {
                    if (ImGui.Button("Cancel"))
                    {
                        _handler.Submit(new BeatCommand { Type = CommandType.Cancel });
                    }

                    float progressFraction = _lastSyncProgressReport.TotalBytes > 0
                        ? _lastSyncProgressReport.ByteFraction
                        : _lastSyncProgressReport.Fraction;

                    int progressPercent = _lastSyncProgressReport.TotalBytes > 0
                        ? _lastSyncProgressReport.BytePercent
                        : _lastSyncProgressReport.Percent;

                    string bytesTransferred = SyncProgressReport.FormatBytes(_lastSyncProgressReport.BytesTransferred);
                    string totalBytes = SyncProgressReport.FormatBytes(_lastSyncProgressReport.TotalBytes);
                    
                    ImGui.Text($"{_lastSyncProgressReport.CompletedCount}/{_lastSyncProgressReport.TotalCount} tracks ({bytesTransferred} / {totalBytes}) | {progressPercent}%");

                    float screenWidth = ImGui.GetWindowViewport().WorkSize.X;
                    float progressWidth = Math.Clamp(screenWidth * .75f, 100f, 600);
                    
                    ImGui.ProgressBar(progressFraction, new Vector2(progressWidth, 30f));
                    ImGui.Text($"{_lastSyncProgressReport.CurrentPath}");
                }
                else if (_guiStateType == GuiStateType.CalculatingDiff)
                {
                    if (ImGui.Button("Cancel##Diff"))
                    {
                        _handler.Submit(new BeatCommand { Type = CommandType.Cancel });
                    }
                    ImGui.SameLine();

                    int frameIndex = (int)(Raylib.GetTime() * AnimationSpeed) % _spinnerFrames.Length;
                    ImGui.TextColored(_warningColor, $"Scanning library for changes{_spinnerFrames[frameIndex]}");
                }
                else
                {
                    ImGui.Checkbox("Dry Run", ref _handler.AppState.IsDryRun);
                    ImGui.SameLine();

                    if (_handler.AppState.IsValid())
                    {
                        if (ImGui.Button("Calculate Diff"))
                        {
                            _guiStateType = GuiStateType.CalculatingDiff;
                            _diffTimedOut = false;
                            
                            ClearMessages();
                            
                            _handler.Submit(new BeatCommand
                            {
                                Type = CommandType.CalculateDiff,
                                TimeoutMs = 0
                            });
                        }
                    }

                    if (_cachedDiffResult.IsValid())
                    {
                        ImGui.SameLine();
                        
                        string syncButtonLabel = _cachedDiffResult.HasSize()
                            ? $"Sync {_cachedDiffResult.Jobs.Count} tracks ({SyncProgressReport.FormatBytes(_cachedDiffResult.TotalDiffBytes)})"
                            : $"Sync {_cachedDiffResult.Jobs.Count} tracks";

                        if (ImGui.Button(syncButtonLabel))
                        {
                            _guiStateType = GuiStateType.Syncing;
                            _lastSyncProgressReport = default;
                            
                            ClearMessages();
                            
                            _handler.Submit(new BeatCommand
                            {
                                Type = CommandType.SyncLibrary,
                                DiffResult =  _cachedDiffResult,
                            });
                        }
                    }

                    if (_diffTimedOut)
                    {
                        ImGui.Spacing();
                        ImGui.TextColored(_warningColor, "! Large library detected (auto-scan timed out >100ms). Click 'Calculate Diff' to perform full scan.");
                    }
                }
            }

            ImGui.End();

            rlImGui.End();			// ends the ImGui content mode. Make all ImGui calls before this
            Raylib.EndDrawing();
            
            
            // Handle Commands
            _handler.ProcessCommands();
            DrainResults();
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
                    HandleCalculateDiffResult(result);
                    break;
                case CommandType.CalculateDiffSizes:
                    HandleCalculateDiffSizesResult(result);
                    break;
                case CommandType.SyncLibrary:
                    HandleSyncResult(result);
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
    

    private static void HandleCalculateDiffResult(BeatCommandResult result)
    {
        _guiStateType = GuiStateType.Idle;
                    
        if (result.ResultType == ResultType.Success)
        {
            _cachedDiffResult = result.DiffResult;
            _diffTimedOut = false;
            _lastInfoMessage = $"Sync Track Count: {result.DiffResult.Jobs.Count} | {result.DiffResult.Elapsed}";
            _lastErrorMessage = null;

            // Trigger progressive background sizing
            if (_cachedDiffResult.IsValid())
            {
                _handler.Submit(new BeatCommand
                {
                    Type = CommandType.CalculateDiffSizes,
                    DiffResult = _cachedDiffResult
                });
            }
        }
        else if (result.IsTimedOut)
        {
            _diffTimedOut = true;
            _cachedDiffResult = default;
            _lastInfoMessage = null;
            _lastErrorMessage = null;
        }
        else if (result.ResultType == ResultType.Cancelled)
        {
            _diffTimedOut = false;
            _lastInfoMessage = "Diff scan cancelled.";
            _lastErrorMessage = null;
        }
    }

    private static void HandleCalculateDiffSizesResult(BeatCommandResult result)
    {
        if (result.ResultType == ResultType.Success && _cachedDiffResult.Jobs == result.DiffResult.Jobs)
        {
            _cachedDiffResult = result.DiffResult;
        }
    }

    private static void HandleSyncResult(BeatCommandResult result)
    {
        _guiStateType = GuiStateType.Idle;
                    
        _lastInfoMessage = $"Completed Sync: {result.SyncResult}";
        Console.WriteLine(_lastInfoMessage);
                    
        _lastErrorMessage = null;
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

    private static void DrawMainWidgets()
    {
        bool areWidgetsDisabled = _guiStateType != GuiStateType.Idle;
        
        if (areWidgetsDisabled)
            ImGui.BeginDisabled();

        ImGui.Text("Scan Streams (Diff / Size):");
        ImGui.SetNextItemWidth(100);
        if (ImGui.DragInt("##ScanStreamCount", ref _tempScanStreamCount, 1f, 1, SyncCore.MaxScanStreams))
        {
            _handler.Submit(new BeatCommand
            {
                Type = CommandType.SetScanStreams,
                StreamCount = _tempScanStreamCount,
            });
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(1-12, overlaps NAS network latency)");

        ImGui.Text("Transfer Streams (Copy):");
        ImGui.SetNextItemWidth(100);
        if (ImGui.DragInt("##TransferStreamCount", ref _tempTransferStreamCount, 1f, 1, SyncCore.MaxTransferStreams))
        {
            _handler.Submit(new BeatCommand
            {
                Type = CommandType.SetTransferStreams,
                StreamCount = _tempTransferStreamCount,
            });
        }
        ImGui.SameLine();
        ImGui.TextDisabled("(1-6, prevents HDD head thrashing & SD card write stalls)");
                
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
                
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
                    _guiStateType = GuiStateType.CalculatingDiff;
                    
                    _isBrowsingSource = false;
                    _tempSourcePath = selected;
                            
                    _handler.Submit(new BeatCommand
                    {
                        Type = CommandType.MountSourceDirectory,
                        Path = selected,
                    });
                            
                    _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 100 });
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
                    _guiStateType = GuiStateType.CalculatingDiff;
                    
                    _isBrowsingTarget = false;
                    _tempTargetPath = selected;
                            
                    _handler.Submit(new BeatCommand
                    {
                        Type = CommandType.MountTargetDirectory,
                        Path = selected,
                    });
                                
                    _handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 100 });
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
                
        if (areWidgetsDisabled)
            ImGui.EndDisabled();
    }

    private static void ClearMessages()
    {
        _lastErrorMessage = null;
        _lastInfoMessage = null;
    }
}