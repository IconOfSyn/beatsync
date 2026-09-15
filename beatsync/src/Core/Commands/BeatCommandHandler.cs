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
                case CommandType.CalculateDiff:
                    HandleCalculateDiff(command);
                    break;
                case CommandType.SyncLibrary:
                    HandleSyncLibrary(command);
                    break;
                case CommandType.Cancel:
                    HandleCancel(command);
                    break;
            }
        }
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
            var diffResult = await SyncCore.BuildDiffAsync(AppState, cts.Token);
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
                var result = await SyncCore.SyncLibraryAsync(AppState, command.DiffResult, progress, cancelToken);

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
}