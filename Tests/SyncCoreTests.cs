using beatsync;

namespace Tests;

public class SyncCoreTests
{
    private BeatState _beatState;
    
    [SetUp]
    public void Setup()
    {
        _beatState.SyncStreamCount = 4;
        _beatState.LibraryPath = "TestData/Music/";
        _beatState.TargetPath = "TestData/TargetSyncFolder/";
    }

    [TearDown]
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
}