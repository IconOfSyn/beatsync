namespace beatsync;

public class BeatCommandHandler
{
    public AppState AppState = new();

    // Progress: written by background sync task, read by GUI each frame
    public SyncProgressReport LatestProgress;

    private readonly Queue<BeatCommand> _commandQueue = new();
    private readonly Queue<BeatCommandResult> _resultQueue = new();
    private readonly List<Task<BeatCommandResult>> _pendingTasks = new();

    private uint _nextCommandId = 1;
    private CancellationTokenSource? _activeOpCancelTokenSource;
    private bool _isManualCancel;

    public uint Submit(BeatCommand command)
    {
        command.Id = _nextCommandId++;
        _commandQueue.Enqueue(command);
        return command.Id;
    }

    public bool TryDequeueResult(out BeatCommandResult result)
    {
        return _resultQueue.TryDequeue(out result);
    }

    public void ProcessCommands()
    {
        // Check if any pending async tasks have completed
        for (int i = _pendingTasks.Count - 1; i >= 0; --i)
        {
            if (_pendingTasks[i].IsCompleted)
            {
                _resultQueue.Enqueue(_pendingTasks[i].Result);
                _pendingTasks.RemoveAt(i);
            }
        }

        // Process queued commands
        while (_commandQueue.TryDequeue(out BeatCommand command))
        {
            switch (command.Type)
            {
                case CommandType.MountSourceDirectory:
                    HandleMountSourceDirectory(command);
                    break;
                case CommandType.MountTargetDirectory:
                    HandleMountTargetDirectory(command);
                    break;
                case CommandType.CalculateDiff:
                    HandleCalculateDiff(command);
                    break;
                case CommandType.CalculateDiffSizes:
                    HandleCalculateDiffSizes(command);
                    break;
                case CommandType.SyncLibrary:
                    HandleSyncLibrary(command);
                    break;
                case CommandType.Cancel:
                    HandleCancel(command);
                    break;
                case CommandType.SetScanStreams:
                    HandleSetScanStreams(command);
                    break;
                case CommandType.SetTransferStreams:
                    HandleSetTransferStreams(command);
                    break;
            }
        }
    }

    private void HandleMountSourceDirectory(BeatCommand command)
    {
        AppState.SourcePath = command.Path ?? "";
    }

    private void HandleMountTargetDirectory(BeatCommand command)
    {
        AppState.TargetPath = command.Path ?? "";
    }

    private void HandleCalculateDiff(BeatCommand command)
    {
        _activeOpCancelTokenSource?.Cancel();
        _activeOpCancelTokenSource = new CancellationTokenSource();
        _isManualCancel = false;

        var cmdId = command.Id;
        var timeoutMs = command.TimeoutMs;
        var cts = _activeOpCancelTokenSource;

        if (timeoutMs > 0)
        {
            cts.CancelAfter(timeoutMs);
        }

        var task = Task.Run(async () =>
        {
            var diffResult = await SyncCore.BuildSyncListAsync(AppState, cts.Token);
            bool isTimedOut = timeoutMs > 0 && cts.IsCancellationRequested && !_isManualCancel;

            return new BeatCommandResult
            {
                CommandId = cmdId,
                CommandType = CommandType.CalculateDiff,
                ResultType = diffResult.ResultType,
                ErrorMessage = isTimedOut
                    ? $"Auto-scan timed out (>{timeoutMs}). Click 'Calculate Diff' to perform full scan."
                    : diffResult.ErrorMessage,
                DiffResult = diffResult,
                IsTimedOut = isTimedOut,
            };
        });

        _pendingTasks.Add(task);
    }

    private void HandleCalculateDiffSizes(BeatCommand command)
    {
        var cmdId = command.Id;
        
        if (command.DiffResult.IsValid())
        {
            _activeOpCancelTokenSource = new CancellationTokenSource();
            var cts = _activeOpCancelTokenSource;
            var cancelToken = cts.Token;

            var task = Task.Run(async () =>
            {
                var sizeResult = await SyncCore.CalculateDiffSizesAsync(AppState, command.DiffResult, cancelToken);
                return new BeatCommandResult
                {
                    CommandId = cmdId,
                    CommandType = CommandType.CalculateDiffSizes,
                    ResultType = sizeResult.ResultType,
                    DiffResult = sizeResult
                };
            });

            _pendingTasks.Add(task);
        }
        else
        {
            _resultQueue.Enqueue(new BeatCommandResult
            {
                CommandId = cmdId,
                CommandType = CommandType.CalculateDiffSizes,
                ResultType = ResultType.Error,
                ErrorMessage = $"Diff Result is not valid",
            });
        }
    }

    private void HandleSyncLibrary(BeatCommand command)
    {
        if (command.DiffResult.IsValid())
        {
            _activeOpCancelTokenSource?.Cancel();
            _activeOpCancelTokenSource = new CancellationTokenSource();
            _isManualCancel = false;
            LatestProgress = default;

            var cmdId = command.Id;
            var cancelToken = _activeOpCancelTokenSource.Token;

            var progress = new Progress<SyncProgressReport>(report => LatestProgress = report);

            var task = Task.Run(async () =>
            {
                var result = await SyncCore.SyncMusicAsync(AppState, command.DiffResult, progress, cancelToken);

                return new BeatCommandResult
                {
                    CommandId = cmdId,
                    CommandType = CommandType.SyncLibrary,
                    ResultType = result.ResultType,
                    SyncResult = result,
                };
            });

            _pendingTasks.Add(task);
        }
        else
        {
            _resultQueue.Enqueue(new BeatCommandResult
            {
                CommandId =  command.Id,
                CommandType = CommandType.SyncLibrary,
                ResultType = ResultType.Error,
                ErrorMessage = "Track diff is not valid"
            });
        }
    }

    private void HandleCancel(BeatCommand command)
    {
        _isManualCancel = true;
        _activeOpCancelTokenSource?.Cancel();
    }

    private void HandleSetScanStreams(BeatCommand command)
    {
        AppState.ScanStreamCount = command.StreamCount;
    }

    private void HandleSetTransferStreams(BeatCommand command)
    {
        AppState.TransferStreamCount = command.StreamCount;
    }
}