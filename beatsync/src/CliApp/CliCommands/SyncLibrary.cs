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
        var diffResult = await SyncCore.BuildSyncListAsync(appState, cancellationToken);
        if (diffResult.ResultType != ResultType.Success)
        {
            throw new CommandException($"Diff Failed: {diffResult.ErrorMessage}", 666);
        }
        
        // Sync library
        var syncTask = SyncCore.SyncMusicAsync(appState, diffResult, null, cancellationToken);
        var syncResult = await syncTask;

        // Write result
        await console.Output.WriteLineAsync($"{syncResult.ResultType} in {syncResult.Elapsed} | {syncResult.SuccessCount}/{syncResult.TotalCount} | failed syncs: {syncResult.FailedCount} | cancelled syncs: {syncResult.CancelledCount}");
    }
}