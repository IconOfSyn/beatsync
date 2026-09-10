namespace beatsync;

public record struct DiffResult
{
    public ResultType ResultType;
    public List<SyncJob> Jobs;
    public string? ErrorMessage;
}
