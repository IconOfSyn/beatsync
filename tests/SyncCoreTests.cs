using beatsync;

namespace beatsync.tests;

public class SyncCoreTests
{
    private BeatState _beatState;
    
    [OneTimeSetUp]
    public void Setup()
    {
        _beatState.SyncStreamCount = 4;
        _beatState.SourcePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Music");
        _beatState.TargetPath = Path.Combine(AppContext.BaseDirectory, "TestData", "TargetSyncFolder");

        ClearTargetSyncDirectory(_beatState.TargetPath);
    }

    private void ClearTargetSyncDirectory(string targetPath)
    {
        var options = new EnumerationOptions()
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            MatchType = MatchType.Simple
            
        };

        var targetEnumerator = Directory.EnumerateFiles(targetPath, "*", options);
        foreach (var file in targetEnumerator)
        {
            File.Delete(file);
        }
    }


    [OneTimeSetUp]
    public void TearDown()
    {
        
    }

    [Test]
    public async Task BasicSyncTest()
    {
        var cancelTokenSource = new CancellationTokenSource();
        var result = await SyncCore.SyncMusicAsync(_beatState, null, cancelTokenSource.Token);
        
        Assert.That(result.ResultType == ResultType.Success);
        Assert.That(result.FailedCount == 0);
    }
    
    [Test]
    public async Task CancelWorksTest()
    {
        var cancelTokenSource = new CancellationTokenSource();
        cancelTokenSource.Cancel();
        
        var result = await SyncCore.SyncMusicAsync(_beatState, null, cancelTokenSource.Token);
        
        Assert.That(result.ResultType == ResultType.Cancelled);
    }
    
    [Test]
    public void TestDataAssetsExist()
    {
        Assert.That(Directory.Exists(_beatState.SourcePath), Is.True, $"LibraryPath does not exist at {_beatState.SourcePath}");
        Assert.That(Directory.Exists(_beatState.TargetPath), Is.True, $"TargetPath does not exist at {_beatState.TargetPath}");
        Assert.That(File.Exists(Path.Combine(_beatState.SourcePath, "AvengedSevenfold", "CityOfEvil", "BeastAndTheHarlot.flac")), Is.True);
        Assert.That(File.Exists(Path.Combine(_beatState.SourcePath, "AvengedSevenfold", "TheStage", "TheStage.flac")), Is.True);
    }
    
    [Test]
    public async Task BuildFileSyncList()
    {
        var syncList = await SyncCore.BuildSyncList(_beatState, default);
        
        Assert.That(syncList != null);
        Assert.That(syncList.Count > 0);
    }
}