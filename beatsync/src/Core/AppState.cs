namespace beatsync;

public record struct AppState()
{
    public const int CurrentVersion = 1;
    public const int DefaultScanStreamCount = 6;
    public const int DefaultTransferStreamCount = 4;

    public int Version = CurrentVersion;
    public string SourcePath = "";
    public string TargetPath = "";
    public int ScanStreamCount = DefaultScanStreamCount;
    public int TransferStreamCount = DefaultTransferStreamCount;
    public bool IsDryRun = false;
    
    
    public bool IsValid() => Version > 0 && !string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(TargetPath);

    public static byte[] Serialize(AppState state)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);
        
        writer.Write(state.Version);
        writer.Write(state.SourcePath ?? string.Empty);
        writer.Write(state.TargetPath ?? string.Empty);
        writer.Write(state.ScanStreamCount);
        writer.Write(state.TransferStreamCount);
        writer.Write(state.IsDryRun);

        return stream.ToArray();
    }
    
    public static AppState Deserialize(byte[] blob)
    {
        AppState appState = new();

        try
        {
            using MemoryStream stream = new MemoryStream(blob);
            using BinaryReader reader = new BinaryReader(stream);

            appState.Version = reader.ReadInt32();
            appState.SourcePath = reader.ReadString(); 
            appState.TargetPath = reader.ReadString(); 
            appState.ScanStreamCount = reader.ReadInt32();
            appState.TransferStreamCount = reader.ReadInt32();
            appState.IsDryRun = reader.ReadBoolean();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

        return appState;
    }
}