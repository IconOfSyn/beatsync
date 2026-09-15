using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;

namespace beatsync.CliCommands;

[Command(Description = "Sync music library to a target directory")]
public partial class SyncLibrary : ICommand
{
    [CommandParameter(0, Name="source", Description = "Music library source directory")]
    public required string  LibraryDirectory { get; set; }
    
    [CommandParameter(1, Name="target", Description = "Target directory")]
    public required string TargetDirectory { get; set; }
    
    [CommandOption("threads", 't', Description = "Number of threads used to sync tracks")]
    public int Threads { get; set; } = SyncCore.MaxTransferStreams;
    
    [CommandOption("dry", 'd', Description = "Do a dry run, don't actually copy files")]
    public bool DryRun { get; set; }
    
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var cancellationToken = console.RegisterCancellationHandler();
        
        var appState = new AppState()
        {
            SourcePath = LibraryDirectory,
            TargetPath = TargetDirectory,
            ScanStreamCount =  Threads,
            TransferStreamCount = Threads,
            IsDryRun =  DryRun,
        };

        // Get diff list
        await console.Output.WriteLineAsync("Scanning libraries...");
        var diffResult = await SyncCore.BuildDiffAsync(appState, cancellationToken);
        if (diffResult.ResultType != ResultType.Success)
        {
            throw new CommandException($"Diff Failed: {diffResult.ErrorMessage}", 666);
        }

        // Check to see if there are no tracks to sync
        if (diffResult.Jobs.Count == 0)
        {
            await console.Output.WriteLineAsync("✓ Everything is up to date. No tracks to sync!");
            return;
        }

        string dryRunSuffix = DryRun ? " [Dry Run]" : "";
        await console.Output.WriteLineAsync($"Found {diffResult.Jobs.Count} track(s) to sync ({SyncProgressReport.FormatBytes(diffResult.TotalDiffBytes)}){dryRunSuffix}.");

        // Sync library
        await using var progress = new ConsoleSyncProgress(console);
        var syncResult = await SyncCore.SyncLibraryAsync(appState, diffResult, progress, cancellationToken);

        // Write result
        await console.Output.WriteLineAsync($"{syncResult.ResultType} in {syncResult.Elapsed:mm\\:ss\\.ff} | {syncResult.SuccessCount}/{syncResult.TotalCount} tracks synced | failed: {syncResult.FailedCount} | cancelled: {syncResult.CancelledCount}");
    }
}