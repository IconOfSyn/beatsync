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

public readonly record struct FileCopyResult(
    FileSyncStatus Status,
    string? ErrorMessage = null
);

public readonly record struct SyncProgressReport(
    int CompletedCount,
    int TotalCount,
    string CurrentPath,
    FileSyncStatus LastFileStatus = FileSyncStatus.Success,
    long BytesTransferred = 0,
    long TotalBytes = 0
)
{
    public float Fraction => TotalCount <= 0 ? 0f : Math.Clamp((float)CompletedCount / TotalCount, 0f, 1f);
    public int Percent => (int)(Fraction * 100);

    public float ByteFraction => TotalBytes <= 0 ? 0f : Math.Clamp((float)BytesTransferred / TotalBytes, 0f, 1f);
    public int BytePercent => (int)(ByteFraction * 100);

    public float ProgressFraction => TotalBytes > 0 ? ByteFraction : Fraction;

    public int ProgressPercent
    {
        get
        {
            int percent = (int)(ProgressFraction * 100);
            if (CompletedCount < TotalCount && percent >= 100)
            {
                return 99;
            }
            return percent;
        }
    }

    public string FormattedProgress =>
        $"{CompletedCount}/{TotalCount} tracks ({FormatBytes(BytesTransferred)} / {FormatBytes(TotalBytes)})";

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
    }
}

public enum ResultType : byte
{
    None, 
    InProgress,
    Success,
    Error,
    Cancelled,
}