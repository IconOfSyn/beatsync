namespace beatsync;

public record struct BeatState
{
    public string LibraryPath;
    public string TargetPath;
    public int SyncStreamCount;
}