using System.Diagnostics;

namespace beatsync;

public static class SyncCore
{
    public const int MaxSyncStreams = 20;
    private const int DryRunJobDelayInMilliseconds = 80;
    
    public static async Task<DiffResult> BuildSyncList(AppState appState, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appState.SourcePath) || !Directory.Exists(appState.SourcePath))
        {
            return new DiffResult
            {
                ResultType = ResultType.Error,
                Jobs = [],
                ErrorMessage = $"Source directory does not exist or is invalid: '{appState.SourcePath}'"
            };
        }

        if (string.IsNullOrWhiteSpace(appState.TargetPath))
        {
            return new DiffResult
            {
                ResultType = ResultType.Error,
                Jobs = [],
                ErrorMessage = $"Target directory path is invalid: '{appState.TargetPath}'"
            };
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return new DiffResult
            {
                ResultType = ResultType.Cancelled,
                Jobs = [],
            };
        }

        try
        {
            var jobs = await Task.Run(() =>
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
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return null;
                        }
                        var relTarget = Path.GetRelativePath(appState.TargetPath, targetFile);
                        existingTargetFiles.Add(relTarget);
                    }
                }

                foreach (var sourceFile in Directory.EnumerateFiles(appState.SourcePath, "*", options))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return null;
                    }

                    var relativePath = Path.GetRelativePath(appState.SourcePath, sourceFile);

                    if (!existingTargetFiles.Contains(relativePath))
                    {
                        long fileSizeBytes = 0;
                        try
                        {
                            fileSizeBytes = new FileInfo(sourceFile).Length;
                        }
                        catch
                        {
                            // Best effort querying file size
                        }

                        syncJobList.Add(new SyncJob
                        {
                            SongPath = relativePath,
                            FileSizeBytes = fileSizeBytes,
                            PercentageComplete = 0f
                        });
                    }
                }

                return syncJobList;
            }, cancellationToken);

            if (jobs is null)
            {
                return new DiffResult
                {
                    ResultType = ResultType.Cancelled,
                    Jobs = [],
                };
            }

            return new DiffResult
            {
                ResultType = ResultType.Success,
                Jobs = jobs,
            };
        }
        catch (OperationCanceledException)
        {
            return new DiffResult
            {
                ResultType = ResultType.Cancelled,
                Jobs = [],
            };
        }
    }
    
    // Lol, SyncAsync...
    public static async Task<SyncResult> SyncMusicAsync(
        AppState appState,
        DiffResult diffResult,
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

        var syncJobList = diffResult.Jobs;
        var fileResults = new FileSyncResult[syncJobList.Count];
        
        int completedCount = 0;
        long totalBytesToTransfer = syncJobList.Sum(j => j.FileSizeBytes);
        long totalBytesTransferred = 0;

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

                if (appState.IsDryRun)
                {
                    try
                    {
                        await Task.Delay(DryRunJobDelayInMilliseconds, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        fileResults[index] = new FileSyncResult(
                            Path: job.SongPath,
                            Started: startTime,
                            Completed: DateTimeOffset.UtcNow,
                            Status: FileSyncStatus.Cancelled
                        );
                        return;
                    }

                    Interlocked.Add(ref totalBytesTransferred, job.FileSizeBytes);
                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: FileSyncStatus.Success
                    );
                }
                else
                {
                    string sourceFullPath = Path.Combine(appState.SourcePath, job.SongPath);
                    string targetFullPath = Path.Combine(appState.TargetPath, job.SongPath);

                    var copyResult = await CopyFileAsync(
                        sourceFullPath,
                        targetFullPath,
                        bytesRead => Interlocked.Add(ref totalBytesTransferred, bytesRead),
                        ct);

                    fileResults[index] = new FileSyncResult(
                        Path: job.SongPath,
                        Started: startTime,
                        Completed: DateTimeOffset.UtcNow,
                        Status: copyResult.Status,
                        ErrorMessage: copyResult.ErrorMessage
                    );
                }

                int completed = Interlocked.Increment(ref completedCount);

                progress?.Report(new SyncProgressReport(
                    completed,
                    syncJobList.Count,
                    job.SongPath,
                    fileResults[index].Status,
                    Interlocked.Read(ref totalBytesTransferred),
                    totalBytesToTransfer
                ));
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

    private const int CopyBufferSize = 128 * 1024; // 128 KB buffer

    private static async Task<FileCopyResult> CopyFileAsync(
        string sourcePath,
        string targetPath,
        Action<int> onBytesRead,
        CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return new FileCopyResult(FileSyncStatus.Cancelled);
        }

        string? targetDir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(targetDir))
        {
            try
            {
                Directory.CreateDirectory(targetDir);
            }
            catch (Exception ex)
            {
                return new FileCopyResult(FileSyncStatus.Failed, $"Failed to create directory '{targetDir}': {ex.Message}");
            }
        }

        string tempTargetPath = targetPath + ".bsyn-tmp";

        try
        {
            var fileOptions = FileOptions.Asynchronous | FileOptions.SequentialScan;

            await using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize, fileOptions))
            await using (var targetStream = new FileStream(tempTargetPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, fileOptions))
            {
                byte[] buffer = new byte[CopyBufferSize];
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await targetStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    onBytesRead(bytesRead);
                }
            }

            // Atomic move into final location
            File.Move(tempTargetPath, targetPath, overwrite: true);

            // Preserve modification timestamp
            File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));

            return new FileCopyResult(FileSyncStatus.Success);
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempTargetPath);
            return new FileCopyResult(FileSyncStatus.Cancelled);
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempTargetPath);
            return new FileCopyResult(FileSyncStatus.Failed, ex.Message);
        }
    }

    private static void CleanupTempFile(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}