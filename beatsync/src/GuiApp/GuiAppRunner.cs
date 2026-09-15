using System.Numerics;
using ImGuiNET;
using Microsoft.Extensions.Logging;
using Raylib_cs;
using rlImGui_cs;

namespace beatsync;

public class GuiAppRunner
{
    private enum GuiStateType : byte
    {
        Idle,
        CalculatingDiff,
        Syncing
    }
    
    // Constants
    private const int MaxDirectoryLength = 1024;
    private static readonly Vector4 _goodColor = new(0.4f, 0.8f, 0.4f, 1f);
    private static readonly Vector4 _warningColor = new(0.9f, 0.7f, 0.2f, 1f);
    private static readonly Vector4 _errorColor = new(0.9f, 0.4f, 0.4f, 1f);

    private static readonly string[] _spinnerFrames = [".  ", ".. ", "..."];
    private const float AnimationSpeed = 3f;

    // Command handler
    private readonly BeatCommandHandler _commandHandler = new();
    private readonly ILogger<SyncCore> _logger;

    // UI State
    private GuiStateType _guiStateType = GuiStateType.Idle;
    private DiffResult _cachedDiffResult;
    private bool _diffTimedOut;
    private string? _lastErrorMessage;
    private string? _lastInfoMessage;

    // Async folder picker task (polled on main thread)
    private Task<string>? _activeFolderPicker;
    private Action<string>? _onFolderPicked;

    
    public GuiAppRunner(ILogger<SyncCore> logger)
    {
        _logger = logger;
    }
    

    public void Run(bool isDryRun)
    {
        Raylib.SetConfigFlags(ConfigFlags.HighDpiWindow | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(850, 480, "Beat Sync");

        rlImGui.Setup(true, false);

        var saveFile = Path.Combine(GetOrCreateWritableDirectory(), "SaveState.bsyn");

        _commandHandler.AppState = LoadAppState(saveFile);
        _commandHandler.AppState.IsDryRun = isDryRun;

        if (_commandHandler.AppState.IsValid())
        {
            StartDiff(timeoutMs: 100);
        }

        while (!Raylib.WindowShouldClose())
        {
            PollFolderPicker();
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
                DrawStatusBanners();

                DrawMainWidgets();

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                DrawActionArea();
            }

            ImGui.End();

            rlImGui.End();			// ends the ImGui content mode. Make all ImGui calls before this
            Raylib.EndDrawing();
            
            
            // Handle Commands
            _commandHandler.ProcessCommands();
            DrainResults();
        }

        rlImGui.Shutdown();		// cleans up ImGui
        Raylib.CloseWindow();
        
        SaveAppState(saveFile, _commandHandler.AppState);
    }

    private void DrainResults()
    {
        while (_commandHandler.TryDequeueResult(out var result))
        {
            switch (result.CommandType)
            {
                case CommandType.CalculateDiff:
                    HandleCalculateDiffResult(result);
                    break;
                case CommandType.SyncLibrary:
                    HandleSyncResult(result);
                    break;
            }

            if (!string.IsNullOrEmpty(result.ErrorMessage) && !result.IsTimedOut)
            {
                _lastErrorMessage = result.ErrorMessage;
                Console.WriteLine(_lastErrorMessage);
            }
        }
    }

    private void HandleCalculateDiffResult(BeatCommandResult result)
    {
        _guiStateType = GuiStateType.Idle;
                    
        if (result.ResultType == ResultType.Success)
        {
            _cachedDiffResult = result.DiffResult;
            _diffTimedOut = false;
            _lastInfoMessage = $"Sync Track Count: {result.DiffResult.Jobs.Count} | {result.DiffResult.Elapsed}";
            _lastErrorMessage = null;
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

    private void HandleSyncResult(BeatCommandResult result)
    {
        _guiStateType = GuiStateType.Idle;
        
        InvalidateDiff();
                    
        _lastInfoMessage = $"Completed Sync: {result.SyncResult}";
        Console.WriteLine(_lastInfoMessage);
    }

    private void PollFolderPicker()
    {
        if (_activeFolderPicker is { IsCompleted: true } task)
        {
            var selected = task.Result;
            var callback = _onFolderPicked;

            _activeFolderPicker = null;
            _onFolderPicked = null;

            if (!string.IsNullOrWhiteSpace(selected))
            {
                callback?.Invoke(selected);
            }
        }
    }

    private  void PickDirectoryAsync(string initialPath, string prompt, Action<string> onComplete)
    {
        if (_activeFolderPicker is not null && !_activeFolderPicker.IsCompleted)
            return;

        _onFolderPicked = onComplete;
        _activeFolderPicker = Task.Run(() => FolderDialog.PickFolder(prompt, initialPath));
    }

    private  void SaveAppState(string saveFile, AppState appState)
    {
        var appStateBlob = AppState.Serialize(appState);
        File.WriteAllBytes(saveFile, appStateBlob);
    }
    
    private  AppState LoadAppState(string loadFile)
    {
        if (!File.Exists(loadFile))
            return new AppState();
        
        try
        {
            var appStateBlob = File.ReadAllBytes(loadFile);
            var appState = AppState.Deserialize(appStateBlob);

            if (appState.Version <= 0 || appState.TargetPath == null || appState.SourcePath == null)
            {
                Console.WriteLine("Loaded app state is malformed, reverting to default");
                return new AppState();
            }
            
            return appState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load app state: {ex.Message}");
            return new AppState();
        }
    }
    
    private static string GetOrCreateWritableDirectory()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string appFolder = Path.Combine(basePath, "EldritchEngineering", "BeatSync");
        Directory.CreateDirectory(appFolder);

        return appFolder;
    }

    private  void DrawStatusBanners()
    {
        if (!string.IsNullOrWhiteSpace(_lastErrorMessage))
        {
            ImGui.TextColored(_errorColor, _lastErrorMessage);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (!string.IsNullOrWhiteSpace(_lastInfoMessage))
        {
            ImGui.TextWrapped(_lastInfoMessage);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    private  void DrawMainWidgets()
    {
        bool areWidgetsDisabled = _guiStateType != GuiStateType.Idle;

        if (areWidgetsDisabled)
            ImGui.BeginDisabled();

        DrawStreamSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawPathSelector(
            "Source Directory:",
            "Source",
            ref _commandHandler.AppState.SourcePath,
            "Select Source Directory",
            "✗ Directory not found",
            _errorColor,
            selected => _commandHandler.AppState.SourcePath = selected);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        DrawPathSelector(
            "Target Directory:",
            "Target",
            ref _commandHandler.AppState.TargetPath,
            "Select Target Directory",
            "! Directory will be created upon sync",
            _warningColor,
            selected => _commandHandler.AppState.TargetPath = selected);

        if (areWidgetsDisabled)
            ImGui.EndDisabled();
    }

    private  void DrawStreamSettings()
    {
        ImGui.Text("Scan Streams (Diff / Size):");
        ImGui.SetNextItemWidth(100);
        ImGui.DragInt("##ScanStreamCount", ref _commandHandler.AppState.ScanStreamCount, 1f, 1, SyncCore.MaxScanStreams);
        ImGui.SameLine();
        ImGui.TextDisabled("(1-12, overlaps NAS network latency)");

        ImGui.Text("Transfer Streams (Copy):");
        ImGui.SetNextItemWidth(100);
        ImGui.DragInt("##TransferStreamCount", ref _commandHandler.AppState.TransferStreamCount, 1f, 1, SyncCore.MaxTransferStreams);
        ImGui.SameLine();
        ImGui.TextDisabled("(1-6, prevents HDD head thrashing & SD card write stalls)");
    }

    private  void DrawPathSelector(
        string label,
        string id,
        ref string path,
        string browsePrompt,
        string notFoundMessage,
        Vector4 notFoundColor,
        Action<string> onSelected)
    {
        ImGui.Text(label);
        ImGui.SetNextItemWidth(450);

        if (ImGui.InputText($"##{id}Path", ref path, MaxDirectoryLength))
        {
            InvalidateDiff();
        }

        ImGui.SameLine();

        bool isBrowsing = _activeFolderPicker is { IsCompleted: false };
        if (isBrowsing)
        {
            ImGui.BeginDisabled();
            ImGui.Button($"Browsing...##{id}");
            ImGui.EndDisabled();
        }
        else if (ImGui.Button($"Browse...##{id}"))
        {
            var currentPath = path;
            PickDirectoryAsync(currentPath, browsePrompt, selected =>
            {
                onSelected(selected);
                InvalidateDiff();
                if (_commandHandler.AppState.IsValid())
                {
                    StartDiff(timeoutMs: 100);
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            if (Directory.Exists(path))
            {
                ImGui.TextColored(_goodColor, "✓ Directory exists");
            }
            else
            {
                ImGui.TextColored(notFoundColor, notFoundMessage);
            }
        }
    }

    private  void DrawActionArea()
    {
        switch (_guiStateType)
        {
            case GuiStateType.Syncing:
                DrawSyncingState();
                break;
            case GuiStateType.CalculatingDiff:
                DrawScanningState();
                break;
            default:
                DrawIdleActions();
                break;
        }
    }

    private  void DrawSyncingState()
    {
        if (ImGui.Button("Cancel"))
        {
            CancelOperation();
        }

        var progress = _commandHandler.LatestProgress;

        float progressFraction = progress.TotalBytes > 0
            ? progress.ByteFraction
            : progress.Fraction;

        int progressPercent = progress.TotalBytes > 0
            ? progress.BytePercent
            : progress.Percent;

        string bytesTransferred = SyncProgressReport.FormatBytes(progress.BytesTransferred);
        string totalBytes = SyncProgressReport.FormatBytes(progress.TotalBytes);

        ImGui.Text($"{progress.CompletedCount}/{progress.TotalCount} tracks ({bytesTransferred} / {totalBytes}) | {progressPercent}%");

        float screenWidth = ImGui.GetWindowViewport().WorkSize.X;
        float progressWidth = Math.Clamp(screenWidth * 0.75f, 100f, 600f);

        ImGui.ProgressBar(progressFraction, new Vector2(progressWidth, 30f));
        ImGui.Text(progress.CurrentPath ?? "");
    }

    private  void DrawScanningState()
    {
        if (ImGui.Button("Cancel##Diff"))
        {
            CancelOperation();
        }
        ImGui.SameLine();

        int frameIndex = (int)(Raylib.GetTime() * AnimationSpeed) % _spinnerFrames.Length;
        ImGui.TextColored(_warningColor, $"Scanning library for changes{_spinnerFrames[frameIndex]}");
    }

    private  void DrawIdleActions()
    {
        ImGui.Checkbox("Dry Run", ref _commandHandler.AppState.IsDryRun);
        ImGui.SameLine();

        if (_commandHandler.AppState.IsValid())
        {
            if (ImGui.Button("Calculate Diff"))
            {
                StartDiff(timeoutMs: 0);
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
                StartSync();
            }
        }

        if (_diffTimedOut)
        {
            ImGui.Spacing();
            ImGui.TextColored(_warningColor, "! Large library detected (auto-scan timed out >100ms). Click 'Calculate Diff' to perform full scan.");
        }
    }

    private  void StartDiff(int timeoutMs)
    {
        _guiStateType = GuiStateType.CalculatingDiff;
        _diffTimedOut = false;
        ClearMessages();

        _commandHandler.Submit(new BeatCommand
        {
            Type = CommandType.CalculateDiff,
            TimeoutMs = timeoutMs
        });
    }

    private  void StartSync()
    {
        _guiStateType = GuiStateType.Syncing;
        ClearMessages();

        _commandHandler.Submit(new BeatCommand
        {
            Type = CommandType.SyncLibrary,
            DiffResult = _cachedDiffResult,
        });
    }

    private  void CancelOperation()
    {
        _commandHandler.Submit(new BeatCommand
        {
            Type = CommandType.Cancel
        });
    }

    private  void InvalidateDiff()
    {
        _cachedDiffResult = default;
        _diffTimedOut = false;
        ClearMessages();
    }

    private  void ClearMessages()
    {
        _lastErrorMessage = null;
        _lastInfoMessage = null;
    }
}