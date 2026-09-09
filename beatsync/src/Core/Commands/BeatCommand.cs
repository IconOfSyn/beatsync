namespace beatsync;

public record struct BeatCommand
{
    public CommandType Type;
    public string LibraryPath;
    public string SyncTargetPath;
}

public enum CommandType : byte
{
    None,
    
    MountLibrary,
    MountSyncTarget,
    SyncLibrary,
    CancelSync,
    SetMaxParallelStreams,
}