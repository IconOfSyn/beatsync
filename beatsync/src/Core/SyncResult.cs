namespace beatsync;

public enum FileSyncStatus : byte
{
    None,
    Success,
    Failed,
    Skipped,
    Cancelled
}

public readonly record struct FileSyncResult(
    string Path,
    DateTimeOffset Started,
    DateTimeOffset Completed,
    FileSyncStatus Status = FileSyncStatus.None,
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
    public int SuccessCount => Files.Count(f => f.Status == FileSyncStatus.Success);
    public int FailedCount => Files.Count(f => f.Status == FileSyncStatus.Failed);
    public int SkippedCount => Files.Count(f => f.Status == FileSyncStatus.Skipped);
    public int CancelledCount => Files.Count(f => f.Status == FileSyncStatus.Cancelled);
    public bool HasFailures => FailedCount > 0;
}

public readonly record struct SyncProgressReport(
    int CompletedCount,
    int TotalCount,
    string CurrentPath,
    FileSyncStatus LastFileStatus = FileSyncStatus.Success
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