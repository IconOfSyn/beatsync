namespace beatsync;

public record struct DiffResult
{
    public ResultType ResultType { get; init; }
    public TimeSpan Elapsed { get; init; }
    public List<SyncJob> Jobs { get; init; }
    public string? ErrorMessage { get; init; }
    public long TotalDiffBytes { get; init; }
}
