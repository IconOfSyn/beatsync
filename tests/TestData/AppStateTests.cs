namespace beatsync.tests;

public class AppStateTests
{
    private static readonly AppState _testAppState = new AppState
    {
        Version = 1,
        SourcePath = "SourceTest",
        TargetPath =  "TargetTest",
        SyncStreamCount = 4,
        IsDryRun = false,
    };

    private static readonly byte[] _testBlob =
    [
        // Version
        1, 0, 0, 0,
        // Source Path
        10, 83, 111, 117, 114, 99, 101, 84, 101, 115, 116, 
        // Target Path
        10, 84, 97, 114, 103, 101, 116, 84, 101, 115, 116,
        // Sync Stream Count
        4, 0, 0, 0,
        // IsDryRun
        0
    ];
    
    
    [Test]
    public void SerializeAppState()
    {
        var blob = AppState.Serialize(_testAppState);
        
        Assert.That(blob.Length == _testBlob.Length);
        Assert.That(blob, Is.EqualTo(_testBlob));
    }
    
    [Test]
    public void DeserializeAppState()
    {
        var appState = AppState.Deserialize(_testBlob);
        
        Assert.That(appState, Is.EqualTo(_testAppState));
    } 
}