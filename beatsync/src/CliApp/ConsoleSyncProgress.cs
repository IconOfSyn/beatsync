using CliFx.Infrastructure;

namespace beatsync;

public sealed class ConsoleSyncProgress : IProgress<SyncProgressReport>, IAsyncDisposable
{
    private readonly IConsole _console;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _renderLoopTask;

    private SyncProgressReport _latestReport;
    private bool _hasReported;
    private bool _renderedOnce;
    private int _spinnerTick;
    private int _lastLoggedMilestone = -1;

    private static readonly char[] SpinnerFrames = ['⠋', '⠙', '⠹', '⠸', '⠼', '⠴', '⠦', '⠧', '⠇', '⠏'];
    private const int DefaultBarWidth = 24;

    public ConsoleSyncProgress(IConsole console)
    {
        _console = console;

        if (!_console.IsOutputRedirected)
        {
            _renderLoopTask = Task.Run(RunRenderLoopAsync);
        }
        else
        {
            _renderLoopTask = Task.CompletedTask;
        }
    }

    public void Report(SyncProgressReport value)
    {
        lock (_lock)
        {
            _latestReport = value;
            _hasReported = true;

            if (_console.IsOutputRedirected)
            {
                RenderRedirectedMilestone(value);
            }
            else if (!_renderedOnce)
            {
                RenderFrame(isFinal: false);
            }
        }
    }

    private async Task RunRenderLoopAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16)); // ~60 FPS

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(_cts.Token))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            lock (_lock)
            {
                if (_hasReported)
                {
                    _spinnerTick++;
                    RenderFrame(isFinal: false);
                }
            }
        }
    }

    private void RenderFrame(bool isFinal)
    {
        int safeWidth = GetSafeWindowWidth();
        int maxLineLength = Math.Max(20, safeWidth - 1);

        char spinner = isFinal ? '✔' : SpinnerFrames[(_spinnerTick / 4) % SpinnerFrames.Length];

        // Format stats: " | 28/42 tracks (103.3 MB / 154.2 MB) ⠋"
        string stats = $" | {_latestReport.CompletedCount}/{_latestReport.TotalCount} tracks ({SyncProgressReport.FormatBytes(_latestReport.BytesTransferred)} / {SyncProgressReport.FormatBytes(_latestReport.TotalBytes)}) {spinner}";
        string percentText = $"{_latestReport.ProgressPercent,3}%";

        // Calculate responsive bar width
        int prefixLen = percentText.Length + stats.Length + 3; // "[", "]", and space
        int availableForBar = maxLineLength - prefixLen;
        int barWidth = Math.Clamp(availableForBar, 8, DefaultBarWidth);

        float fraction = _latestReport.ProgressFraction;
        int filled = (int)Math.Round(fraction * barWidth);
        filled = Math.Clamp(filled, 0, barWidth);
        int empty = barWidth - filled;

        string bar = new string('█', filled) + new string('░', empty);
        string line1 = $"[{bar}] {percentText}{stats}";

        if (line1.Length > maxLineLength)
        {
            line1 = line1[..maxLineLength];
        }

        // Line 2: "↳ Pink Floyd/The Dark Side of the Moon/05 - Money.mp3"
        string currentPath = _latestReport.CurrentPath ?? string.Empty;
        string line2Prefix = "↳ ";
        string line2;

        int maxPathLen = maxLineLength - line2Prefix.Length;
        if (currentPath.Length > maxPathLen && maxPathLen > 5)
        {
            line2 = $"{line2Prefix}...{currentPath[^Math.Max(0, maxPathLen - 3)..]}";
        }
        else
        {
            line2 = $"{line2Prefix}{currentPath}";
        }

        if (line2.Length > maxLineLength)
        {
            line2 = line2[..maxLineLength];
        }

        // Single-blit ANSI frame write to avoid console tearing
        if (!_renderedOnce)
        {
            _renderedOnce = true;
            _console.Output.Write($"{line1}\n{line2}");
        }
        else
        {
            // Cursor up 1 line (\x1b[1A), carriage return (\r), clear line 1 (\x1b[2K), line 1,
            // newline carriage return (\n\r), clear line 2 (\x1b[2K), line 2.
            _console.Output.Write($"\x1b[1A\r\x1b[2K{line1}\n\r\x1b[2K{line2}");
        }
    }

    private void RenderRedirectedMilestone(SyncProgressReport report)
    {
        // Log milestone lines every 10% or on completion
        int milestone = report.ProgressPercent / 10;
        if (milestone > _lastLoggedMilestone || report.CompletedCount >= report.TotalCount)
        {
            _lastLoggedMilestone = milestone;
            _console.Output.WriteLine($"[Sync] {report.ProgressPercent}% | {report.CompletedCount}/{report.TotalCount} tracks ({SyncProgressReport.FormatBytes(report.BytesTransferred)} / {SyncProgressReport.FormatBytes(report.TotalBytes)})");
        }
    }

    private int GetSafeWindowWidth()
    {
        try
        {
            int width = _console.WindowWidth;
            return width > 10 ? width : 80;
        }
        catch
        {
            return 80;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        try
        {
            await _renderLoopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown
        }

        if (!_console.IsOutputRedirected && _hasReported)
        {
            lock (_lock)
            {
                RenderFrame(isFinal: true);
            }
            _console.Output.WriteLine();
        }

        _cts.Dispose();
    }
}
