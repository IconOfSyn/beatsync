using System.Diagnostics;
using System.Runtime.InteropServices;

namespace beatsync;

public static class FolderDialog
{
    public static string PickFolder(string prompt = "Select Directory", string? initialPath = null)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return PickFolderMac(prompt, initialPath);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return PickFolderWindows(prompt, initialPath);
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return PickFolderLinux(prompt, initialPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error showing native folder dialog: {ex.Message}");
        }

        return "";
    }

    private static string PickFolderMac(string prompt, string? initialPath)
    {
        string escapedPrompt = prompt.Replace("\"", "\\\"");
        string script;
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            string escapedPath = Path.GetFullPath(initialPath).Replace("\"", "\\\"");
            script = $"POSIX path of (choose folder with prompt \"{escapedPrompt}\" default location POSIX file \"{escapedPath}\")";
        }
        else
        {
            script = $"POSIX path of (choose folder with prompt \"{escapedPrompt}\")";
        }

        var psi = new ProcessStartInfo
        {
            FileName = "osascript",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi);
        if (process is null) 
            return "";

        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output.TrimEnd('/', '\\');
        }

        return "";
    }

    private static string PickFolderWindows(string prompt, string? initialPath)
    {
        string initDirArg = !string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath)
            ? $"$f.SelectedPath = '{Path.GetFullPath(initialPath).Replace("'", "''")}';"
            : "";

        string command = $"Add-Type -AssemblyName System.Windows.Forms; $f = New-Object System.Windows.Forms.FolderBrowserDialog; $f.Description = '{prompt.Replace("'", "''")}'; {initDirArg} if ($f.ShowDialog() -eq 'OK') {{ Write-Output $f.SelectedPath }}";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(command);

        using var process = Process.Start(psi);
        if (process is null)
            return "";

        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output;
        }

        return "";
    }

    private static string PickFolderLinux(string prompt, string? initialPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "zenity",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--file-selection");
        psi.ArgumentList.Add("--directory");
        psi.ArgumentList.Add($"--title={prompt}");

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            psi.ArgumentList.Add($"--filename={Path.GetFullPath(initialPath)}");
        }

        using var process = Process.Start(psi);
        if (process is null) 
            return "";

        string output = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();

        if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
        {
            return output.TrimEnd('/', '\\');
        }

        return "";
    }
}
