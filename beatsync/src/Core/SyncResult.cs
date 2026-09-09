namespace beatsync;

public enum SyncStatus : byte
{
    Success,
    Failed,
    Skipped,
    Cancelled
}

public readonly record struct FileSyncResult(
    string Path,
    DateTimeOffset Started,
    DateTimeOffset Completed,
    SyncStatus Status = SyncStatus.Success,
    string? ErrorMessage = null
)
{
    public TimeSpan Duration => Completed - Started;
}

public sealed record SyncResult
{
    public required ResultType ResultType { get; init; }
    public required IReadOnlyList<FileSyncResult> Files { get; init; }
    public TimeSpan Elapsed { get; init; }

    public int TotalCount => Files.Count;
    public int SuccessCount => Files.Count(f => f.Status == SyncStatus.Success);
    public int FailedCount => Files.Count(f => f.Status == SyncStatus.Failed);
    public int SkippedCount => Files.Count(f => f.Status == SyncStatus.Skipped);
    public int CancelledCount => Files.Count(f => f.Status == SyncStatus.Cancelled);
    public bool HasFailures => FailedCount > 0;
}

public readonly record struct SyncProgressReport(
    int CompletedCount,
    int TotalCount,
    string CurrentPath,
    SyncStatus LastFileStatus = SyncStatus.Success
)
{
    public float Fraction => TotalCount == 0 ? 0f : (float)CompletedCount / TotalCount;
    public int Percent => (int)(Fraction * 100);
}

public enum ResultType : byte
{
    None, 
    InProgress,
    Success,
    Error,
    Cancelled,
}