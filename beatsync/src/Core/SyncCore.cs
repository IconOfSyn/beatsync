using System.Diagnostics;
using System.IO.Enumeration;

namespace beatsync;

public static class SyncCore
{
    public const int MaxScanStreams = 12;
    public const int MaxTransferStreams = 6;
    private const int DryRunJobDelayInMilliseconds = 80;
    private const int InitialFileCollectionCapacity = 4096;
    
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

            int parallelism = Math.Clamp(appState.ScanStreamCount, 1, MaxScanStreams);

            // 1. Enumerate Target Files into HashSet<string> in parallel
            var targetStopwatch = Stopwatch.StartNew();
            var existingTargetFiles = EnumerateTargetFiles(appState.TargetPath, parallelism, options, cancellationToken);
            targetStopwatch.Stop();

            if (cancellationToken.IsCancellationRequested)
            {
                return [];
            }

            // 2. Enumerate Source Files in parallel
            var sourceStopwatch = Stopwatch.StartNew();
            var syncJobList = EnumerateSourceFiles(appState.SourcePath, existingTargetFiles, parallelism, options, cancellationToken);
            sourceStopwatch.Stop();

            if (cancellationToken.IsCancellationRequested)
            {
                return [];
            }

            Console.WriteLine($"[Diff] Target: {targetStopwatch.ElapsedMilliseconds}ms ({existingTargetFiles.Count} files), " +
                              $"Source: {sourceStopwatch.ElapsedMilliseconds}ms ({syncJobList.Count} diff files)");

            return syncJobList;
        }, cancellationToken);

        return createSyncListTask;
    }

    private static bool ShouldIncludeFile(ref FileSystemEntry entry) =>
        !entry.IsDirectory && (entry.FileName.Length == 0 || entry.FileName[0] != '.');

    private static bool ShouldRecurseDirectory(ref FileSystemEntry entry) =>
        entry.IsDirectory && (entry.FileName.Length == 0 || entry.FileName[0] != '.');

    private static string GetRelativePath(ref FileSystemEntry entry, int prefixLen)
    {
        ReadOnlySpan<char> relDir = entry.Directory.Length > prefixLen
            ? entry.Directory.Slice(prefixLen)
            : ReadOnlySpan<char>.Empty;

        return Path.Join(relDir, entry.FileName);
    }

    private readonly record struct DirectoryPartitionResult(
        List<string> Subtrees,
        List<string> RootFiles
    );

    private static DirectoryPartitionResult DiscoverSubtrees(
        string rootPath,
        int minPartitions,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath) || cancellationToken.IsCancellationRequested)
        {
            return new DirectoryPartitionResult([], []);
        }

        var subtrees = new List<string>();
        var rootFiles = new List<string>();
        int prefixLen = GetPrefixLength(rootPath);
        var queue = new Queue<string>();
        queue.Enqueue(rootPath);

        var nonRecursiveOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = true,
            AttributesToSkip = (FileAttributes)0
        };

        while (queue.Count > 0 && (queue.Count + subtrees.Count) < minPartitions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            string currentDir = queue.Dequeue();
            var subDirs = new List<string>();

            CollectDirectoryEntries(currentDir, prefixLen, nonRecursiveOptions, subDirs, rootFiles, cancellationToken);

            if (subDirs.Count == 0)
            {
                // Leaf folder; any files were captured into rootFiles
            }
            else if (queue.Count + subDirs.Count + subtrees.Count <= minPartitions)
            {
                foreach (var dir in subDirs)
                {
                    queue.Enqueue(dir);
                }
            }
            else
            {
                subtrees.AddRange(subDirs);
            }
        }

        while (queue.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            subtrees.Add(queue.Dequeue());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            subtrees.Clear();
            rootFiles.Clear();
        }

        return new DirectoryPartitionResult(subtrees, rootFiles);
    }

    private static void CollectDirectoryEntries(
        string currentDir,
        int prefixLen,
        EnumerationOptions options,
        List<string> subDirs,
        List<string> rootFiles,
        CancellationToken cancellationToken)
    {
        var enumerable = new FileSystemEnumerable<bool>(
            currentDir,
            (ref FileSystemEntry entry) =>
            {
                if (entry.FileName.Length == 0 || entry.FileName[0] == '.')
                    return true;

                if (entry.IsDirectory)
                {
                    subDirs.Add(Path.Combine(currentDir, entry.FileName.ToString()));
                }
                else
                {
                    rootFiles.Add(GetRelativePath(ref entry, prefixLen));
                }

                return true;
            },
            options);

        foreach (var _ in enumerable)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private static List<string> EnumerateTargetSubtree(
        string subDir,
        int targetPrefixLen,
        EnumerationOptions options,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        var enumerable = new FileSystemEnumerable<string>(
            subDir,
            (ref FileSystemEntry entry) => GetRelativePath(ref entry, targetPrefixLen),
            options)
        {
            ShouldIncludePredicate = ShouldIncludeFile,
            ShouldRecursePredicate = ShouldRecurseDirectory
        };

        foreach (var relPath in enumerable)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            files.Add(relPath);
        }

        return files;
    }

    private static List<SyncJob> EnumerateSourceSubtree(
        string subDir,
        int sourcePrefixLen,
        HashSet<string>.AlternateLookup<ReadOnlySpan<char>> targetLookup,
        EnumerationOptions options,
        CancellationToken cancellationToken)
    {
        var jobs = new List<SyncJob>();

        var enumerable = new FileSystemEnumerable<bool>(
            subDir,
            (ref FileSystemEntry entry) =>
            {
                ReadOnlySpan<char> relDir = entry.Directory.Length > sourcePrefixLen
                    ? entry.Directory.Slice(sourcePrefixLen)
                    : ReadOnlySpan<char>.Empty;

                Span<char> pathBuffer = stackalloc char[1024];
                if (!Path.TryJoin(relDir, entry.FileName, pathBuffer, out int charsWritten))
                    return true;

                ReadOnlySpan<char> relPath = pathBuffer.Slice(0, charsWritten);
                if (!targetLookup.Contains(relPath))
                {
                    jobs.Add(new SyncJob
                    {
                        SongPath = relPath.ToString(),
                        FileSizeBytes = 0,
                        PercentageComplete = 0f
                    });
                }

                return true;
            },
            options)
        {
            ShouldIncludePredicate = ShouldIncludeFile,
            ShouldRecursePredicate = ShouldRecurseDirectory
        };

        foreach (var _ in enumerable)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return jobs;
    }

    private static HashSet<string> EnumerateTargetFiles(
        string targetPath,
        int parallelism,
        EnumerationOptions options,
        CancellationToken cancellationToken)
    {
        var existingTargetFiles = new HashSet<string>(InitialFileCollectionCapacity, StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(targetPath) || cancellationToken.IsCancellationRequested)
        {
            return existingTargetFiles;
        }

        var partition = DiscoverSubtrees(targetPath, parallelism * 2, cancellationToken);
        foreach (var rootFile in partition.RootFiles)
        {
            existingTargetFiles.Add(rootFile);
        }

        if (partition.Subtrees.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var targetLock = new object();
            int targetPrefixLen = GetPrefixLength(targetPath);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = parallelism };

            Parallel.ForEach(partition.Subtrees, parallelOptions, (subDir, state) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    state.Stop();
                    return;
                }

                var files = EnumerateTargetSubtree(subDir, targetPrefixLen, options, cancellationToken);
                if (files.Count > 0)
                {
                    lock (targetLock)
                    {
                        existingTargetFiles.UnionWith(files);
                    }
                }
            });
        }

        return existingTargetFiles;
    }

    private static List<SyncJob> EnumerateSourceFiles(
        string sourcePath,
        HashSet<string> existingTargetFiles,
        int parallelism,
        EnumerationOptions options,
        CancellationToken cancellationToken)
    {
        var syncJobList = new List<SyncJob>(InitialFileCollectionCapacity);

        if (!Directory.Exists(sourcePath) || cancellationToken.IsCancellationRequested)
        {
            return syncJobList;
        }

        var partition = DiscoverSubtrees(sourcePath, parallelism * 2, cancellationToken);
        var targetLookup = existingTargetFiles.GetAlternateLookup<ReadOnlySpan<char>>();

        foreach (var rootFile in partition.RootFiles)
        {
            if (!existingTargetFiles.Contains(rootFile))
            {
                syncJobList.Add(new SyncJob
                {
                    SongPath = rootFile,
                    FileSizeBytes = 0,
                    PercentageComplete = 0f
                });
            }
        }

        if (partition.Subtrees.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var syncJobLock = new object();
            int sourcePrefixLen = GetPrefixLength(sourcePath);
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = parallelism };

            Parallel.ForEach(partition.Subtrees, parallelOptions, (subDir, state) =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    state.Stop();
                    return;
                }

                var jobs = EnumerateSourceSubtree(subDir, sourcePrefixLen, targetLookup, options, cancellationToken);
                if (jobs.Count > 0)
                {
                    lock (syncJobLock)
                    {
                        syncJobList.AddRange(jobs);
                    }
                }
            });
        }

        return syncJobList;
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
            MaxDegreeOfParallelism = Math.Clamp(appState.ScanStreamCount, 1, MaxScanStreams)
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
        int syncStreamCount = Math.Clamp(appState.TransferStreamCount, 1, MaxTransferStreams);

        
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
                // Meh, I tried :shrug:
            }
        }
    }
}