using System.Diagnostics;

namespace beatsync;

public static class SyncCore
{
    public const int MaxSyncStreams = 20;
    
    public static async Task<List<SyncJob>> BuildSyncList(BeatState beatState)
    {
        var syncJobList = new List<SyncJob>();
        syncJobList.Add(new SyncJob { SongPath = "AvengedSevenfold/CityOfEvil/BeastAndTheHarlot.flac" });
        syncJobList.Add(new SyncJob { SongPath = "AvengedSevenfold/TheStage/TheStage.flac" });

        return syncJobList;
    }
    
    // Lol, SyncAsync...
    public static async Task<SyncResult> SyncMusicAsync(
        BeatState beatState,
        IProgress<SyncProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int syncStreamCount = beatState.SyncStreamCount;
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
        
        var syncJobList = await BuildSyncList(beatState);
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
                        Status: SyncStatus.Skipped
                    );
                    
                    return;
                }

                var startTime = DateTimeOffset.UtcNow;

                try
                {
                    // Simulated song processing/transfer
                    await Task.Delay(500, ct);

                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: SyncStatus.Success
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: SyncStatus.Cancelled
                    );
                }
                catch (Exception ex)
                {
                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: SyncStatus.Failed,
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
                    Status: SyncStatus.Skipped
                );
            }
        }

        ResultType resultType;
        if (cancellationToken.IsCancellationRequested)
        {
            resultType = ResultType.Cancelled;
        }
        else if (fileResults.Any(f => f.Status == SyncStatus.Failed))
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