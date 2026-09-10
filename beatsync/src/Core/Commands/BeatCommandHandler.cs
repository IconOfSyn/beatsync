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
    private CancellationTokenSource? _syncCancelTokenSource;

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
                case CommandType.SyncLibrary:
                    HandleSyncLibrary(command);
                    break;
                case CommandType.CancelSync:
                    HandleCancelSync(command);
                    break;
                case CommandType.SetMaxParallelStreams:
                    HandleSetMaxParallelStreams(command);
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
        var cmdId = command.Id;

        var task = Task.Run(async () =>
        {
            var diffResult = await SyncCore.BuildSyncList(AppState);

            return new BeatCommandResult
            {
                CommandId = cmdId,
                CommandType = CommandType.CalculateDiff,
                ResultType = diffResult.ResultType,
                ErrorMessage = diffResult.ErrorMessage,
                DiffList = diffResult.Jobs,
            };
        });

        _pendingTasks.Add(task);
    }

    private void HandleSyncLibrary(BeatCommand command)
    {
        _syncCancelTokenSource = new CancellationTokenSource();
        LatestProgress = default;

        var cmdId = command.Id;
        var isDryRun = command.IsDryRun;
        var cancelToken = _syncCancelTokenSource.Token;

        var progress = new Progress<SyncProgressReport>(report => LatestProgress = report);

        var task = Task.Run(async () =>
        {
            var result = await SyncCore.SyncMusicAsync(AppState, isDryRun, progress, cancelToken);

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

    private void HandleCancelSync(BeatCommand command)
    {
        _syncCancelTokenSource?.Cancel();
    }

    private void HandleSetMaxParallelStreams(BeatCommand command)
    {
        AppState.SyncStreamCount = command.StreamCount;
    }
}