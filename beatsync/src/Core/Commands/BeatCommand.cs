namespace beatsync;

public record struct BeatCommand
{
    public uint Id;
    public CommandType Type;
    public int TimeoutMs;
    public DiffResult DiffResult;
}

public enum CommandType : byte
{
    None,
    
    CalculateDiff,
    CalculateDiffSizes,
    SyncLibrary,
    Cancel,
}