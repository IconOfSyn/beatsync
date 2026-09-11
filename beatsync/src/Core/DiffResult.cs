namespace beatsync;

public record struct DiffResult
{
    public ResultType ResultType { get; init; }
    public TimeSpan Elapsed { get; init; }
    public List<SyncJob> Jobs { get; init; }
    public string? ErrorMessage { get; init; }
    public long TotalDiffBytes { get; init; }
    public bool IsValid() => ResultType == ResultType.Success && Jobs is { Count: > 0 };
    public bool HasSize() => IsValid() && TotalDiffBytes > 0;
}
