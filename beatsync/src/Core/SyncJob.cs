namespace beatsync;

public record struct SyncJob
{
    public string SongPath;
    public long FileSizeBytes;
    public float PercentageComplete;
}