namespace beatsync;

public record struct BeatCommand
{
    public uint Id;
    public CommandType Type;
    public string? Path;
    public bool IsDryRun;
    public int StreamCount;
    public int TimeoutMs;
    public DiffResult DiffResult;
}

public enum CommandType : byte
{
    None,
    
    MountSourceDirectory,
    MountTargetDirectory,
    CalculateDiff,
    SyncLibrary,
    Cancel,
    SetMaxParallelStreams,
}