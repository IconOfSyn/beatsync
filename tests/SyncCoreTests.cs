using System.Diagnostics;
using beatsync;

namespace beatsync.tests;

public class SyncCoreTests
{
    private AppState _appState;
    
    [OneTimeSetUp]
    public void Setup()
    {
        _appState.SyncStreamCount = 4;
        _appState.IsDryRun = true;
        _appState.SourcePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Music");
        _appState.TargetPath = Path.Combine(AppContext.BaseDirectory, "TestData", "TargetSyncFolder");

        ClearTargetSyncDirectory(_appState.TargetPath);
    }

    [SetUp]
    public void TestSetUp()
    {
        ClearTargetSyncDirectory(_appState.TargetPath);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        ClearTargetSyncDirectory(_appState.TargetPath);
    }

    private void ClearTargetSyncDirectory(string? targetPath)
    {
        if (!Directory.Exists(targetPath)) 
            return;

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            MatchType = MatchType.Simple
        };

        foreach (var file in Directory.EnumerateFiles(targetPath, "*", options))
        {
            File.Delete(file);
        }

        foreach (var dir in Directory.EnumerateDirectories(targetPath, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
        {
            Directory.Delete(dir, false);
        }
    }

    [Test]
    public async Task BasicSyncTest()
    {
        var cancelTokenSource = new CancellationTokenSource();
        
        var diffResult = await SyncCore.BuildSyncList(_appState, cancelTokenSource.Token);
        var syncResult = await SyncCore.SyncMusicAsync(_appState, diffResult, null, cancelTokenSource.Token);
        
        Assert.That(syncResult.ResultType == ResultType.Success);
        Assert.That(syncResult.FailedCount == 0);
    }

    [Test]
    public async Task RealSync_CopiesFilesToTarget()
    {
        var cancelTokenSource = new CancellationTokenSource();
        
        _appState.IsDryRun = false;
        var diffResult = await SyncCore.BuildSyncList(_appState, cancelTokenSource.Token);
        var result = await SyncCore.SyncMusicAsync(_appState, diffResult, null, cancelTokenSource.Token);

        Assert.That(result.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(result.SuccessCount, Is.EqualTo(2));
        Assert.That(result.FailedCount, Is.EqualTo(0));

        var targetFile1 = Path.Combine(_appState.TargetPath!, "AvengedSevenfold", "CityOfEvil", "BeastAndTheHarlot.flac");
        var targetFile2 = Path.Combine(_appState.TargetPath!, "AvengedSevenfold", "TheStage", "TheStage.flac");

        Assert.That(File.Exists(targetFile1), Is.True, "Target file 1 should exist");
        Assert.That(File.Exists(targetFile2), Is.True, "Target file 2 should exist");

        var sourceFile1 = Path.Combine(_appState.SourcePath!, "AvengedSevenfold", "CityOfEvil", "BeastAndTheHarlot.flac");
        Assert.That(new FileInfo(targetFile1).Length, Is.EqualTo(new FileInfo(sourceFile1).Length));
    }

    [Test]
    public async Task RealSync_WhenCancelled_CleansUpTempFiles()
    {
        var cancelTokenSource = new CancellationTokenSource();
        cancelTokenSource.Cancel();

        _appState.IsDryRun = false;
        var diffResult = await SyncCore.BuildSyncList(_appState, cancelTokenSource.Token);
        var result = await SyncCore.SyncMusicAsync(_appState, diffResult, null, cancelTokenSource.Token);

        Assert.That(result.ResultType, Is.EqualTo(ResultType.Cancelled));

        if (Directory.Exists(_appState.TargetPath))
        {
            var tempFiles = Directory.GetFiles(_appState.TargetPath, "*.bsyn-tmp", SearchOption.AllDirectories);
            Assert.That(tempFiles, Is.Empty);
        }
    }
    
    [Test]
    public async Task CancelWorksTest()
    {
        var cancelTokenSource = new CancellationTokenSource();
        
        var diffResult = await SyncCore.BuildSyncList(_appState, cancelTokenSource.Token);
        await cancelTokenSource.CancelAsync();
        var result = await SyncCore.SyncMusicAsync(_appState, diffResult, null, cancelTokenSource.Token);
        
        Assert.That(result.ResultType == ResultType.Cancelled);
    }
    
    [Test]
    public async Task BuildFileSyncList()
    {
        var diffResult = await SyncCore.BuildSyncList(_appState, default);
        
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Is.Not.Null);
        Assert.That(diffResult.Jobs.Count > 0);
    }

    [Test]
    public async Task BuildSyncList_WhenTargetEmpty_ReturnsAllSourceFiles()
    {
        var diffResult = await SyncCore.BuildSyncList(_appState, default);

        var expectedFile1 = Path.Combine("AvengedSevenfold", "CityOfEvil", "BeastAndTheHarlot.flac");
        var expectedFile2 = Path.Combine("AvengedSevenfold", "TheStage", "TheStage.flac");

        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Has.Count.EqualTo(2));
        Assert.That(diffResult.Jobs.Select(j => j.SongPath), Does.Contain(expectedFile1));
        Assert.That(diffResult.Jobs.Select(j => j.SongPath), Does.Contain(expectedFile2));
    }

    [Test]
    public async Task BuildSyncList_WhenFileAlreadyInTarget_ExcludesExistingFile()
    {
        var existingRelative = Path.Combine("AvengedSevenfold", "CityOfEvil", "BeastAndTheHarlot.flac");
        Debug.Assert(_appState.TargetPath != null);
        
        var targetFile = Path.Combine(_appState.TargetPath, existingRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
        await File.WriteAllTextAsync(targetFile, "dummy flac content");

        var diffResult = await SyncCore.BuildSyncList(_appState, default);

        var missingRelative = Path.Combine("AvengedSevenfold", "TheStage", "TheStage.flac");
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Has.Count.EqualTo(1));
        Assert.That(diffResult.Jobs[0].SongPath, Is.EqualTo(missingRelative));
    }

    [Test]
    public async Task BuildSyncList_WhenAllFilesPresentInTarget_ReturnsEmptyList()
    {
        Debug.Assert(_appState.TargetPath != null);
        
        var file1 = Path.Combine(_appState.TargetPath, "AvengedSevenfold", "CityOfEvil", "BeastAndTheHarlot.flac");
        var file2 = Path.Combine(_appState.TargetPath, "AvengedSevenfold", "TheStage", "TheStage.flac");
        
        Directory.CreateDirectory(Path.GetDirectoryName(file1)!);
        Directory.CreateDirectory(Path.GetDirectoryName(file2)!);
        
        await File.WriteAllTextAsync(file1, "dummy");
        await File.WriteAllTextAsync(file2, "dummy");

        var diffResult = await SyncCore.BuildSyncList(_appState, default);

        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Is.Empty);
    }

    [Test]
    public async Task BuildSyncList_WhenCancelled_ReturnsCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var diffResult = await SyncCore.BuildSyncList(_appState, cts.Token);
        
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Cancelled));
    }

    [Test]
    public async Task BuildSyncList_WhenSourceDoesNotExist_ReturnsErrorResult()
    {
        var invalidState = _appState with { SourcePath = Path.Combine(AppContext.BaseDirectory, "NonExistentSource") };
        var diffResult = await SyncCore.BuildSyncList(invalidState, default);

        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Error));
        Assert.That(diffResult.Jobs, Is.Empty);
        Assert.That(diffResult.ErrorMessage, Is.Not.Null);
    }

    [Test]
    public async Task BuildSyncList_WhenTargetDoesNotExist_TreatsAllFilesAsMissing()
    {
        var nonExistentTarget = Path.Combine(AppContext.BaseDirectory, "TestData", "NonExistentTarget_" + Guid.NewGuid().ToString("N"));
        var state = _appState with { TargetPath = nonExistentTarget };

        try
        {
            var diffResult = await SyncCore.BuildSyncList(state, default);
            Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(diffResult.Jobs, Has.Count.EqualTo(2));
        }
        finally
        {
            if (Directory.Exists(nonExistentTarget))
            {
                Directory.Delete(nonExistentTarget, true);
            }
        }
    }

    [Test]
    public async Task BuildSyncList_WhenTimedOut_CancelsGracefully()
    {
        using var cts = new CancellationTokenSource(1);
        await Task.Delay(10); // Ensure CTS has elapsed

        var diffResult = await SyncCore.BuildSyncList(_appState, cts.Token);
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Cancelled));
    }

    [Test]
    public async Task BeatCommandHandler_AutoDiff_TimesOutQuickly()
    {
        // Use the project directory which contains thousands of files in obj/bin/.git to ensure scan takes >1ms
        var largeDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var handler = new BeatCommandHandler { AppState = _appState with { SourcePath = largeDir } };
        handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 1 });
        handler.ProcessCommands(); // Dispatch into pending tasks

        // Allow timeout to fire and background task to complete
        await Task.Delay(100);
        handler.ProcessCommands(); // Collect completed result

        Assert.That(handler.TryDequeueResult(out var result), Is.True);
        Assert.That(result.CommandType, Is.EqualTo(CommandType.CalculateDiff));
        Assert.That(result.ResultType, Is.EqualTo(ResultType.Cancelled));
        Assert.That(result.IsTimedOut, Is.True);
    }

    [Test]
    public async Task BeatCommandHandler_CancelCommand_CancelsDiff()
    {
        var handler = new BeatCommandHandler { AppState = _appState };
        handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 0 });
        handler.Submit(new BeatCommand { Type = CommandType.Cancel });

        handler.ProcessCommands();
        await Task.Delay(50);
        handler.ProcessCommands();

        Assert.That(handler.TryDequeueResult(out var result), Is.True);
        Assert.That(result.ResultType, Is.EqualTo(ResultType.Cancelled));
    }
}