namespace beatsync;

public class BeatCommandHandler
{
    public BeatState BeatState = new();
    public  IProgress<SyncProgressReport> SyncProgress = new Progress<SyncProgressReport>();

    private readonly Queue<BeatCommand> _commandQueue = new();


    private CancellationTokenSource _syncCancelTokenSource;
    private bool _isSyncInProgress;


    public void AddCommand(BeatCommand command)
    {
        _commandQueue.Enqueue(command);
    }

    public async Task ProcessCommands()
    {
        while (_commandQueue.TryDequeue(out BeatCommand command))
        {
            switch (command.Type)
            {
                case CommandType.MountLibrary:
                    HandleMountLibrary(command);
                    break;
                case CommandType.MountSyncTarget:
                    HandleMountSyncTarget(command);
                    break;
                case CommandType.SyncLibrary:
                    await HandleSyncLibrary(command);
                    break;
                case CommandType.CancelSync:
                    HandleCancelSync(command);
                    break;
            }
        }
    }

    private void HandleMountLibrary(BeatCommand command)
    {
        BeatState.LibraryPath = command.LibraryPath;
    }

    private void HandleMountSyncTarget(BeatCommand command)
    {
        BeatState.TargetPath = command.LibraryPath;
    }

    private async Task HandleSyncLibrary(BeatCommand command)
    {
        _syncCancelTokenSource = new CancellationTokenSource();
        await SyncCore.SyncMusicAsync(BeatState, SyncProgress, _syncCancelTokenSource.Token);
    }

    private void HandleCancelSync(BeatCommand command)
    {
        _syncCancelTokenSource?.Cancel();
    }

    
}