using System.Diagnostics;
using System.IO.Enumeration;

namespace beatsync;

public static class SyncCore
{
    public const int MaxSyncStreams = 20;
    private const int DryRunJobDelayInMilliseconds = 80;
    
    public static async Task<DiffResult> BuildSyncListAsync(AppState appState, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var resultType = ResultType.InProgress;
        string? errorMessage = null;
        List<SyncJob> jobs = [];

        if (string.IsNullOrWhiteSpace(appState.SourcePath) || !Directory.Exists(appState.SourcePath))
        {
            resultType = ResultType.Error;
            errorMessage = $"Source directory does not exist or is invalid: '{appState.SourcePath}'";
        }
        else if (string.IsNullOrWhiteSpace(appState.TargetPath))
        {
            resultType = ResultType.Error;
            errorMessage = $"Target directory path is invalid: '{appState.TargetPath}'";
        }
        else if (cancellationToken.IsCancellationRequested)
        {
            resultType = ResultType.Cancelled;
        }
        else
        {
            try
            {
                var createSyncListTask = CreateSyncListTask(appState, cancellationToken);
                jobs = await createSyncListTask; 

                resultType = cancellationToken.IsCancellationRequested ? ResultType.Cancelled : ResultType.Success;
            }
            catch (OperationCanceledException)
            {
                resultType = ResultType.Cancelled;
            }
        }

        long totalDiffBytes = 0;

        stopwatch.Stop();

        return new DiffResult
        {
            ResultType = resultType,
            Jobs = jobs,
            TotalDiffBytes = totalDiffBytes,
            ErrorMessage = errorMessage,
            Elapsed = stopwatch.Elapsed,
        };
    }

    private static Task<List<SyncJob>> CreateSyncListTask(AppState appState, CancellationToken cancellationToken)
    {
        var createSyncListTask = Task.Run(() =>
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = (FileAttributes)0
            };

            // 1. Enumerate Target Files into HashSet<string>
            var existingTargetFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(appState.TargetPath))
            {
                int targetPrefixLen = GetPrefixLength(appState.TargetPath);

                var targetEnumerable = new FileSystemEnumerable<string>(
                    appState.TargetPath,
                    (ref FileSystemEntry entry) =>
                    {
                        ReadOnlySpan<char> relDir = entry.Directory.Length > targetPrefixLen
                            ? entry.Directory.Slice(targetPrefixLen)
                            : ReadOnlySpan<char>.Empty;

                        return Path.Join(relDir, entry.FileName);
                    },
                    options)
                {
                    ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                        !entry.IsDirectory && (entry.FileName.Length == 0 || entry.FileName[0] != '.'),
                    ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                        entry.IsDirectory && (entry.FileName.Length == 0 || entry.FileName[0] != '.')
                };

                foreach (var relTarget in targetEnumerable)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return [];
                    }
                    existingTargetFiles.Add(relTarget);
                }
            }

            // 2. Enumerate Source Files with fast span slicing (zero stat calls)
            var syncJobList = new List<SyncJob>();
            int sourcePrefixLen = GetPrefixLength(appState.SourcePath);

            var sourceEnumerable = new FileSystemEnumerable<bool>(
                appState.SourcePath,
                (ref FileSystemEntry entry) =>
                {
                    ReadOnlySpan<char> relDir = entry.Directory.Length > sourcePrefixLen
                        ? entry.Directory.Slice(sourcePrefixLen)
                        : ReadOnlySpan<char>.Empty;

                    string relPath = Path.Join(relDir, entry.FileName);

                    if (!existingTargetFiles.Contains(relPath))
                    {
                        syncJobList.Add(new SyncJob
                        {
                            SongPath = relPath,
                            FileSizeBytes = 0,
                            PercentageComplete = 0f
                        });
                    }

                    return true;
                },
                options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry entry) =>
                    !entry.IsDirectory && (entry.FileName.Length == 0 || entry.FileName[0] != '.'),
                ShouldRecursePredicate = (ref FileSystemEntry entry) =>
                    entry.IsDirectory && (entry.FileName.Length == 0 || entry.FileName[0] != '.')
            };

            foreach (var _ in sourceEnumerable)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return [];
                }
            }

            return syncJobList;
        }, cancellationToken);

        return createSyncListTask;
    }

    private static int GetPrefixLength(string rootPath)
    {
        int len = rootPath.Length;
        if (!rootPath.EndsWith(Path.DirectorySeparatorChar) && !rootPath.EndsWith(Path.AltDirectorySeparatorChar))
        {
            len++;
        }
        return len;
    }
    
    public static async Task<DiffResult> CalculateDiffSizesAsync(
        AppState appState,
        DiffResult diffResult,
        CancellationToken cancellationToken = default)
    {
        if (!diffResult.IsValid())
            return diffResult;

        var jobs = diffResult.Jobs;
        long totalBytes = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Clamp(appState.SyncStreamCount, 1, MaxSyncStreams)
        };

        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, jobs.Count), parallelOptions, (index, ct) =>
            {
                var job = jobs[index];
                string fullPath = Path.Combine(appState.SourcePath, job.SongPath);
                try
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Exists)
                    {
                        job.FileSizeBytes = fileInfo.Length;
                        jobs[index] = job;
                        Interlocked.Add(ref totalBytes, job.FileSizeBytes);
                    }
                }
                catch
                {
                    // Ignore inaccessible or deleted file
                }

                return ValueTask.CompletedTask;
            });

            return diffResult with { TotalDiffBytes = totalBytes };
        }
        catch (OperationCanceledException)
        {
            return diffResult;
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
        long totalBytesToTransfer = diffResult.TotalDiffBytes;
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