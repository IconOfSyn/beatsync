using System.Diagnostics;
using beatsync;

namespace beatsync.tests;

public class SyncCoreTests
{
    private AppState _appState;
    
    [OneTimeSetUp]
    public void Setup()
    {
        _appState.ScanStreamCount = 6;
        _appState.TransferStreamCount = 4;
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
        
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, cancelTokenSource.Token);
        var syncResult = await SyncCore.SyncMusicAsync(_appState, diffResult, null, cancelTokenSource.Token);
        
        Assert.That(syncResult.ResultType == ResultType.Success);
        Assert.That(syncResult.FailedCount == 0);
    }

    [Test]
    public async Task RealSync_CopiesFilesToTarget()
    {
        var cancelTokenSource = new CancellationTokenSource();
        
        _appState.IsDryRun = false;
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, cancelTokenSource.Token);
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
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, cancelTokenSource.Token);
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
        
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, cancelTokenSource.Token);
        await cancelTokenSource.CancelAsync();
        var result = await SyncCore.SyncMusicAsync(_appState, diffResult, null, cancelTokenSource.Token);
        
        Assert.That(result.ResultType == ResultType.Cancelled);
    }
    
    [Test]
    public async Task BuildFileSyncList()
    {
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, default);
        
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Is.Not.Null);
        Assert.That(diffResult.Jobs.Count > 0);
    }

    [Test]
    public async Task BuildSyncList_WhenTargetEmpty_ReturnsAllSourceFiles()
    {
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, default);

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

        var diffResult = await SyncCore.BuildSyncListAsync(_appState, default);

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

        var diffResult = await SyncCore.BuildSyncListAsync(_appState, default);

        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Is.Empty);
    }

    [Test]
    public async Task BuildSyncList_WhenCancelled_ReturnsCancelledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var diffResult = await SyncCore.BuildSyncListAsync(_appState, cts.Token);
        
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Cancelled));
    }

    [Test]
    public async Task BuildSyncList_WhenSourceDoesNotExist_ReturnsErrorResult()
    {
        var invalidState = _appState with { SourcePath = Path.Combine(AppContext.BaseDirectory, "NonExistentSource") };
        var diffResult = await SyncCore.BuildSyncListAsync(invalidState, default);

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
            var diffResult = await SyncCore.BuildSyncListAsync(state, default);
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

        var diffResult = await SyncCore.BuildSyncListAsync(_appState, cts.Token);
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

    [Test]
    public async Task BuildSyncList_PerformanceBenchmark()
    {
        var tempSource = Path.Combine(Path.GetTempPath(), "BeatSync_Bench_Source_" + Guid.NewGuid().ToString("N"));
        var tempTarget = Path.Combine(Path.GetTempPath(), "BeatSync_Bench_Target_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempSource);
            Directory.CreateDirectory(tempTarget);

            // Generate 1,000 files in 50 subdirectories
            for (int dir = 0; dir < 50; dir++)
            {
                var subDir = Path.Combine(tempSource, $"Artist_{dir}", $"Album_{dir}");
                Directory.CreateDirectory(subDir);
                for (int file = 0; file < 20; file++)
                {
                    File.WriteAllText(Path.Combine(subDir, $"Track_{file}.flac"), "dummy");
                }
            }

            var state = new AppState
            {
                SourcePath = tempSource,
                TargetPath = tempTarget,
            };

            var diffResult = await SyncCore.BuildSyncListAsync(state, default);

            Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(diffResult.Jobs.Count, Is.EqualTo(1000));
            // Assert execution completes under 500ms for 1,000 files
            Assert.That(diffResult.Elapsed.TotalMilliseconds, Is.LessThan(500));
            Console.WriteLine($"[BENCHMARK] 1,000 files scanned in {diffResult.Elapsed.TotalMilliseconds:F2} ms");
        }
        finally
        {
            if (Directory.Exists(tempSource)) Directory.Delete(tempSource, true);
            if (Directory.Exists(tempTarget)) Directory.Delete(tempTarget, true);
        }
    }

    [Test]
    public async Task CalculateDiffSizes_EnrichesJobsWithByteCounts()
    {
        var diffResult = await SyncCore.BuildSyncListAsync(_appState, default);
        Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffResult.Jobs, Has.Count.EqualTo(2));
        Assert.That(diffResult.TotalDiffBytes, Is.EqualTo(0));
        Assert.That(diffResult.Jobs[0].FileSizeBytes, Is.EqualTo(0));

        var sizeResult = await SyncCore.CalculateDiffSizesAsync(_appState, diffResult, default);

        Assert.That(sizeResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(sizeResult.TotalDiffBytes, Is.GreaterThan(0));
        Assert.That(sizeResult.Jobs[0].FileSizeBytes, Is.GreaterThan(0));
        Assert.That(sizeResult.Jobs[1].FileSizeBytes, Is.GreaterThan(0));
        Assert.That(sizeResult.TotalDiffBytes, Is.EqualTo(sizeResult.Jobs[0].FileSizeBytes + sizeResult.Jobs[1].FileSizeBytes));
    }

    [Test]
    public async Task BuildSyncList_ExcludesDotFilesAndDirectories_WithoutStat()
    {
        var tempSource = Path.Combine(Path.GetTempPath(), "BeatSync_DotTest_Source_" + Guid.NewGuid().ToString("N"));
        var tempTarget = Path.Combine(Path.GetTempPath(), "BeatSync_DotTest_Target_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempSource);
            Directory.CreateDirectory(tempTarget);

            // Valid song
            var albumDir = Path.Combine(tempSource, "Artist", "Album");
            Directory.CreateDirectory(albumDir);
            File.WriteAllText(Path.Combine(albumDir, "Track.flac"), "audio content");

            // Hidden dot-files
            File.WriteAllText(Path.Combine(albumDir, ".DS_Store"), "metadata");
            File.WriteAllText(Path.Combine(albumDir, "._Track.flac"), "resource fork");

            // Hidden dot-directories
            var gitDir = Path.Combine(tempSource, ".git", "objects");
            Directory.CreateDirectory(gitDir);
            File.WriteAllText(Path.Combine(gitDir, "commit"), "git data");

            var trashDir = Path.Combine(tempSource, ".Trashes", "501");
            Directory.CreateDirectory(trashDir);
            File.WriteAllText(Path.Combine(trashDir, "deleted.mp3"), "deleted data");

            var state = new AppState { SourcePath = tempSource, TargetPath = tempTarget };
            var diffResult = await SyncCore.BuildSyncListAsync(state, default);

            Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
            Assert.That(diffResult.Jobs, Has.Count.EqualTo(1));
            Assert.That(diffResult.Jobs[0].SongPath, Is.EqualTo(Path.Join("Artist", "Album", "Track.flac")));
        }
        finally
        {
            if (Directory.Exists(tempSource)) Directory.Delete(tempSource, true);
            if (Directory.Exists(tempTarget)) Directory.Delete(tempTarget, true);
        }
    }

    [Test]
    public async Task BeatCommandHandler_CalculateDiffSizes_UpdatesDiffResult()
    {
        var handler = new BeatCommandHandler { AppState = _appState };
        handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 0 });
        handler.ProcessCommands();

        await Task.Delay(50);
        handler.ProcessCommands();

        Assert.That(handler.TryDequeueResult(out var diffCmdResult), Is.True);
        Assert.That(diffCmdResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(diffCmdResult.DiffResult.Jobs, Has.Count.EqualTo(2));
        Assert.That(diffCmdResult.DiffResult.TotalDiffBytes, Is.EqualTo(0));

        handler.Submit(new BeatCommand
        {
            Type = CommandType.CalculateDiffSizes,
            DiffResult = diffCmdResult.DiffResult
        });
        handler.ProcessCommands();

        await Task.Delay(50);
        handler.ProcessCommands();

        Assert.That(handler.TryDequeueResult(out var sizeCmdResult), Is.True);
        Assert.That(sizeCmdResult.CommandType, Is.EqualTo(CommandType.CalculateDiffSizes));
        Assert.That(sizeCmdResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(sizeCmdResult.DiffResult.TotalDiffBytes, Is.GreaterThan(0));
        Assert.That(sizeCmdResult.DiffResult.HasSize, Is.True);
    }

    [Test]
    public void DiffResult_IsValid_And_HasSize_Behavior()
    {
        var validNoSize = new DiffResult
        {
            ResultType = ResultType.Success,
            Jobs = [new SyncJob { SongPath = "test.mp3" }],
            TotalDiffBytes = 0
        };
        Assert.That(validNoSize.IsValid, Is.True);
        Assert.That(validNoSize.HasSize, Is.False);

        var validWithSize = validNoSize with { TotalDiffBytes = 1024 };
        Assert.That(validWithSize.IsValid, Is.True);
        Assert.That(validWithSize.HasSize, Is.True);

        var emptyJobs = validNoSize with { Jobs = [] };
        Assert.That(emptyJobs.IsValid, Is.False);
        Assert.That(emptyJobs.HasSize, Is.False);

        var cancelled = validWithSize with { ResultType = ResultType.Cancelled };
        Assert.That(cancelled.IsValid, Is.False);
        Assert.That(cancelled.HasSize, Is.False);

        var error = validWithSize with { ResultType = ResultType.Error };
        Assert.That(error.IsValid, Is.False);
        Assert.That(error.HasSize, Is.False);

        var defaultResult = default(DiffResult);
        Assert.That(defaultResult.IsValid, Is.False);
        Assert.That(defaultResult.HasSize, Is.False);
    }

    [Test]
    public void SyncProgressReport_ProgressFraction_And_ProgressPercent()
    {
        // 1. Zero total bytes: uses track count
        var trackReport = new SyncProgressReport(
            CompletedCount: 5,
            TotalCount: 10,
            CurrentPath: "song.mp3",
            BytesTransferred: 0,
            TotalBytes: 0
        );
        Assert.That(trackReport.ProgressFraction, Is.EqualTo(0.5f));
        Assert.That(trackReport.ProgressPercent, Is.EqualTo(50));

        // 2. Normal byte progress
        var byteReport = new SyncProgressReport(
            CompletedCount: 2,
            TotalCount: 10,
            CurrentPath: "song.mp3",
            BytesTransferred: 500,
            TotalBytes: 1000
        );
        Assert.That(byteReport.ProgressFraction, Is.EqualTo(0.5f));
        Assert.That(byteReport.ProgressPercent, Is.EqualTo(50));

        // 3. Bytes transferred exceed total bytes while tracks are incomplete:
        // Fraction is clamped to 1.0f, but ProgressPercent caps at 99%
        var overByteReport = new SyncProgressReport(
            CompletedCount: 3,
            TotalCount: 10,
            CurrentPath: "song.mp3",
            BytesTransferred: 1500,
            TotalBytes: 1000
        );
        Assert.That(overByteReport.ProgressFraction, Is.EqualTo(1.0f));
        Assert.That(overByteReport.ProgressPercent, Is.EqualTo(99));

        // 4. All tracks complete: reaches 100%
        var finishedReport = new SyncProgressReport(
            CompletedCount: 10,
            TotalCount: 10,
            CurrentPath: "song.mp3",
            BytesTransferred: 1000,
            TotalBytes: 1000
        );
        Assert.That(finishedReport.ProgressFraction, Is.EqualTo(1.0f));
        Assert.That(finishedReport.ProgressPercent, Is.EqualTo(100));
    }

    [Test]
    public async Task CalculateDiffSizes_AfterTimedDiff_DoesNotCancelEarly()
    {
        var handler = new BeatCommandHandler { AppState = _appState };
        // Diff with 30ms timeout
        handler.Submit(new BeatCommand { Type = CommandType.CalculateDiff, TimeoutMs = 30 });
        handler.ProcessCommands();

        await Task.Delay(10);
        handler.ProcessCommands();

        Assert.That(handler.TryDequeueResult(out var diffCmdResult), Is.True);
        Assert.That(diffCmdResult.ResultType, Is.EqualTo(ResultType.Success));

        // Sizing command dispatched
        handler.Submit(new BeatCommand
        {
            Type = CommandType.CalculateDiffSizes,
            DiffResult = diffCmdResult.DiffResult
        });
        handler.ProcessCommands();

        // Wait longer than the original 30ms timeout to ensure sizing isn't cancelled
        await Task.Delay(80);
        handler.ProcessCommands();

        Assert.That(handler.TryDequeueResult(out var sizeCmdResult), Is.True);
        Assert.That(sizeCmdResult.CommandType, Is.EqualTo(CommandType.CalculateDiffSizes));
        Assert.That(sizeCmdResult.ResultType, Is.EqualTo(ResultType.Success));
        Assert.That(sizeCmdResult.DiffResult.HasSize, Is.True);
        Assert.That(sizeCmdResult.DiffResult.TotalDiffBytes, Is.GreaterThan(0));
    }

    [Test]
    public async Task BuildSyncList_Multithreaded_DiscoversAllFilesAcrossNestedDirectories()
    {
        var tempSource = Path.Combine(Path.GetTempPath(), "BeatSync_Multi_Source_" + Guid.NewGuid().ToString("N"));
        var tempTarget = Path.Combine(Path.GetTempPath(), "BeatSync_Multi_Target_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempSource);
            Directory.CreateDirectory(tempTarget);

            // 1 loose root file
            await File.WriteAllTextAsync(Path.Combine(tempSource, "root_song.mp3"), "dummy");

            // 10 artists, 3 albums each, 2 tracks each = 60 tracks
            for (int a = 0; a < 10; a++)
            {
                for (int al = 0; al < 3; al++)
                {
                    var albumDir = Path.Combine(tempSource, $"Artist_{a}", $"Album_{al}");
                    Directory.CreateDirectory(albumDir);
                    for (int t = 0; t < 2; t++)
                    {
                        await File.WriteAllTextAsync(Path.Combine(albumDir, $"Track_{t}.flac"), "audio data");
                    }
                }
            }

            // Put 1 file into target: Artist_0/Album_0/Track_0.flac
            var targetExistingDir = Path.Combine(tempTarget, "Artist_0", "Album_0");
            Directory.CreateDirectory(targetExistingDir);
            await File.WriteAllTextAsync(Path.Combine(targetExistingDir, "Track_0.flac"), "already synced");

            var state = new AppState
            {
                SourcePath = tempSource,
                TargetPath = tempTarget,
                ScanStreamCount = 8,
                TransferStreamCount = 4
            };

            var diffResult = await SyncCore.BuildSyncListAsync(state, default);

            Assert.That(diffResult.ResultType, Is.EqualTo(ResultType.Success));
            // 61 total source files - 1 in target = 60 diff jobs
            Assert.That(diffResult.Jobs, Has.Count.EqualTo(60));

            var jobPaths = diffResult.Jobs.Select(j => j.SongPath).ToHashSet();
            Assert.That(jobPaths, Does.Contain("root_song.mp3"));
            Assert.That(jobPaths, Does.Contain(Path.Join("Artist_0", "Album_0", "Track_1.flac")));
            Assert.That(jobPaths, Does.Contain(Path.Join("Artist_9", "Album_2", "Track_1.flac")));
            Assert.That(jobPaths, Does.Not.Contain(Path.Join("Artist_0", "Album_0", "Track_0.flac")));
        }
        finally
        {
            if (Directory.Exists(tempSource)) Directory.Delete(tempSource, true);
            if (Directory.Exists(tempTarget)) Directory.Delete(tempTarget, true);
        }
    }
}