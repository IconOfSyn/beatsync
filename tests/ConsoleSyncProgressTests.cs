using CliFx.Infrastructure;
using NUnit.Framework;

namespace beatsync.tests;

public class ConsoleSyncProgressTests
{
    [Test]
    public async Task Report_WhenOutputRedirected_PrintsMilestones()
    {
        var console = new FakeInMemoryConsole();
        // FakeInMemoryConsole has IsOutputRedirected = true by default (streams are MemoryStream/StringWriter)
        
        await using (var progress = new ConsoleSyncProgress(console))
        {
            progress.Report(new SyncProgressReport(
                CompletedCount: 5,
                TotalCount: 10,
                CurrentPath: "Artist/Album/Track05.mp3",
                LastFileStatus: FileSyncStatus.Success,
                BytesTransferred: 50 * 1024 * 1024,
                TotalBytes: 100 * 1024 * 1024
            ));

            progress.Report(new SyncProgressReport(
                CompletedCount: 10,
                TotalCount: 10,
                CurrentPath: "Artist/Album/Track10.mp3",
                LastFileStatus: FileSyncStatus.Success,
                BytesTransferred: 100 * 1024 * 1024,
                TotalBytes: 100 * 1024 * 1024
            ));
        }

        var output = console.ReadOutputString();
        Assert.That(output, Does.Contain("[Sync] 50%"));
        Assert.That(output, Does.Contain("[Sync] 100%"));
        Assert.That(output, Does.Contain("5/10 tracks"));
        Assert.That(output, Does.Contain("10/10 tracks"));
    }

    [Test]
    public async Task Report_HandlesMultipleReports_WithoutThrowing()
    {
        var console = new FakeInMemoryConsole();

        await using (var progress = new ConsoleSyncProgress(console))
        {
            for (int i = 1; i <= 20; i++)
            {
                progress.Report(new SyncProgressReport(
                    CompletedCount: i,
                    TotalCount: 20,
                    CurrentPath: $"Track_{i}.flac",
                    LastFileStatus: FileSyncStatus.Success,
                    BytesTransferred: i * 5 * 1024 * 1024,
                    TotalBytes: 100 * 1024 * 1024
                ));
            }
        }

        var output = console.ReadOutputString();
        Assert.That(output, Does.Contain("[Sync] 100%"));
    }
}
