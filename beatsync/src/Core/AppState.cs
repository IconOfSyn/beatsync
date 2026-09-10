namespace beatsync;

public record struct AppState()
{
    public const int CurrentVersion = 1;
    public const int DefaultSyncStreamCount = 4;

    public int Version = CurrentVersion;
    public string SourcePath = "";
    public string TargetPath = "";
    public int SyncStreamCount = DefaultSyncStreamCount;
    
    
    public bool HasValidPaths() => !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(TargetPath);

    public static byte[] Serialize(AppState state)
    {
        // Wrap it in a BinaryWriter
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);
        
        // Write each field in a specific order
        writer.Write(state.Version);
        writer.Write(state.SourcePath ?? string.Empty);
        writer.Write(state.TargetPath ?? string.Empty);
        writer.Write(state.SyncStreamCount);

        // Return the perfectly sized byte array
        return stream.ToArray();
    }
    
    public static AppState Deserialize(byte[] blob)
    {
        AppState appState = new();

        using MemoryStream stream = new MemoryStream(blob);
        using BinaryReader reader = new BinaryReader(stream);

        appState.Version = reader.ReadInt32();
        appState.SourcePath = reader.ReadString(); 
        appState.TargetPath = reader.ReadString(); 
        appState.SyncStreamCount = reader.ReadInt32();

        return appState;
    }
}