using System.Diagnostics;

namespace beatsync;

public static class LogFileUtils
{
    private const string LogFilePattern = "worklog-*.log";

    public static string GetLogDirectory()
    {
        string basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string logDir = Path.Combine(basePath, "EldritchEngineering", "BeatSync", "logs");
        Directory.CreateDirectory(logDir);
        return logDir;
    }

    public static string? FindMostRecentLogFile(string directory)
    {
        if (!Directory.Exists(directory))
            return null;

        return Directory.EnumerateFiles(directory, LogFilePattern)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    public static bool OpenFileInDefaultApp(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
