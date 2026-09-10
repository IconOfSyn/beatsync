using System.Diagnostics;

namespace beatsync;

public static class SyncCore
{
    public const int MaxSyncStreams = 20;
    
    public static async Task<List<SyncJob>> BuildSyncList(AppState appState, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appState.SourcePath) || !Directory.Exists(appState.SourcePath))
        {
            Console.WriteLine($"Source directory does not exist or is invalid: '{appState.SourcePath}'");
            return [];
        }

        if (string.IsNullOrWhiteSpace(appState.TargetPath))
        {
            Console.WriteLine($"Target directory path is invalid: '{appState.TargetPath}'");
            return [];
        }

        return await Task.Run(() =>
        {
            var syncJobList = new List<SyncJob>();

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
            };

            var existingTargetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(appState.TargetPath))
            {
                foreach (var targetFile in Directory.EnumerateFiles(appState.TargetPath, "*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relTarget = Path.GetRelativePath(appState.TargetPath, targetFile);
                    existingTargetFiles.Add(relTarget);
                }
            }

            foreach (var sourceFile in Directory.EnumerateFiles(appState.SourcePath, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var relativePath = Path.GetRelativePath(appState.SourcePath, sourceFile);

                if (!existingTargetFiles.Contains(relativePath))
                {
                    syncJobList.Add(new SyncJob
                    {
                        SongPath = relativePath,
                        PercentageComplete = 0f
                    });
                }
            }

            return syncJobList;
        }, cancellationToken);
    }
    
    // Lol, SyncAsync...
    public static async Task<SyncResult> SyncMusicAsync(
        AppState appState,
        bool isDryRun,
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int syncStreamCount = appState.SyncStreamCount;
        if (syncStreamCount <= 0)
        {
            Console.WriteLine("MaxSyncStreams must be greater than 0, setting to 1");
            syncStreamCount = 1;
        }
        
        if (syncStreamCount > MaxSyncStreams)
        {
            Console.WriteLine($"MaxSyncStreams must be less than max {MaxSyncStreams}, setting to {MaxSyncStreams}");
            syncStreamCount = MaxSyncStreams;
        }

        
        var stopwatch = Stopwatch.StartNew();
        
        List<SyncJob> syncJobList;
        try
        {
            syncJobList = await BuildSyncList(appState, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Console.Write("Cancellation Complete");
            stopwatch.Stop();
            return new SyncResult
            {
                ResultType = ResultType.Cancelled,
                Files = [],
                Elapsed = stopwatch.Elapsed
            };
        }
        
        var fileResults = new FileSyncResult[syncJobList.Count];
        int completedCount = 0;

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = syncStreamCount,
        };

        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, syncJobList.Count), parallelOptions, async (index, ct) =>
            {
                var job = syncJobList[index];

                if (cancellationToken.IsCancellationRequested)
                {
                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: DateTimeOffset.UtcNow,
                        Completed: DateTimeOffset.UtcNow,
                        Status: FileSyncStatus.Skipped
                    );
                    
                    return;
                }

                var startTime = DateTimeOffset.UtcNow;

                try
                {
                    if (isDryRun)
                    {
                        // Simulated song processing/transfer
                        await Task.Delay(80, ct);
                    }
                    else
                    {
                        //Actually do transfer
                    }

                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: FileSyncStatus.Success
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: FileSyncStatus.Cancelled
                    );
                }
                catch (Exception ex)
                {
                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: FileSyncStatus.Failed,
                        ErrorMessage: ex.Message
                    );
                }
                finally
                {
                    int completed = Interlocked.Increment(ref completedCount);
                    
                    progress?.Report(new SyncProgressReport(
                        completed,
                        syncJobList.Count,
                        job.SongPath,
                        fileResults[index].Status
                    ));
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected cancellation - do not treat as an unhandled crash
            Console.Write("Cancellation Complete");
        }

        stopwatch.Stop();

        // Mark any unreached slots as Skipped
        for (int i = 0; i < fileResults.Length; i++)
        {
            if (fileResults[i].Path is null)
            {
                fileResults[i] = new FileSyncResult(
                    Path: syncJobList[i].SongPath,
                    Started: DateTimeOffset.UtcNow,
                    Completed: DateTimeOffset.UtcNow,
                    Status: FileSyncStatus.Skipped
                );
            }
        }

        ResultType resultType;
        if (cancellationToken.IsCancellationRequested)
        {
            resultType = ResultType.Cancelled;
        }
        else if (fileResults.Any(f => f.Status == FileSyncStatus.Failed))
        {
            resultType = ResultType.Error;
        }
        else
        {
            resultType = ResultType.Success;
        }

        return new SyncResult
        {
            ResultType = resultType,
            Files = fileResults,
            Elapsed = stopwatch.Elapsed
        };
    }


}