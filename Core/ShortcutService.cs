using System;
using System.IO;
using System.Runtime.InteropServices;

namespace DeployPaladin.Core;

public class ShortcutService
{
    public void CreateShortcut(string shortcutPath, string targetExe, string name, string iconPath)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;

        // Ensure .lnk extension
        if (!shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            shortcutPath = Path.Combine(shortcutPath, name + ".lnk");

        string? dir = Path.GetDirectoryName(shortcutPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // Use PowerShell to create the shortcut (avoids COM interop dependency)
        string escapedTarget = targetExe.Replace("'", "''");
        string escapedShortcut = shortcutPath.Replace("'", "''");
        string escapedWorkDir = (Path.GetDirectoryName(targetExe) ?? "").Replace("'", "''");

        string script = $"$ws = New-Object -ComObject WScript.Shell; " +
                         $"$s = $ws.CreateShortcut('{escapedShortcut}'); " +
                         $"$s.TargetPath = '{escapedTarget}'; " +
                         $"$s.WorkingDirectory = '{escapedWorkDir}'; ";

        if (!string.IsNullOrEmpty(iconPath))
        {
            string escapedIcon = iconPath.Replace("'", "''");
            script += $"$s.IconLocation = '{escapedIcon}'; ";
        }

        script += "$s.Save()";

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            var proc = System.Diagnostics.Process.Start(psi);
            proc?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Shortcut Error: {ex.Message}");
        }
    }
}
