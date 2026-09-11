namespace beatsync;

public record struct BeatCommandResult
{
    public uint CommandId;
    public CommandType CommandType;
    public ResultType ResultType;
    public string? ErrorMessage;
    public DiffResult DiffResult;
    public SyncResult? SyncResult;
    public bool IsTimedOut;
}
