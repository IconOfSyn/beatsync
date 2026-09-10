namespace beatsync;

public record struct BeatState
{
    public string SourcePath;
    public string TargetPath;
    public int SyncStreamCount;
}