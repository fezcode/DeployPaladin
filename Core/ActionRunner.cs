using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace DeployPaladin.Core;

public class ActionRunner
{
    private readonly FileService _fileService;
    private readonly RegistryService _registryService = new();
    private readonly ShortcutService _shortcutService = new();

    public ActionRunner(BundleReader bundle)
    {
        _fileService = new FileService(bundle);
    }

    public async Task RunActions(List<InstallerAction> actions, string installDir, Action<int, string> onProgress)
    {
        for (int i = 0; i < actions.Count; i++)
        {
            var action = actions[i];

            if (action.IsOptional && !action.IsSelected)
                continue;

            string src = ExpandVariables(action.Source, installDir);
            string dest = ExpandVariables(action.Destination, installDir);

            string description = action.Type switch
            {
                ActionType.MkDir => $"Creating directory: {dest}",
                ActionType.CopyFiles => $"Copying: {action.Source}",
                ActionType.CreateRegistry => $"Writing registry: {action.RegistryKey}",
                ActionType.CreateShortcut => $"Creating shortcut: {action.ShortcutName}",
                _ => "Processing..."
            };

            int progress = (int)((float)(i + 1) / actions.Count * 100);
            onProgress(progress, description);

            switch (action.Type)
            {
                case ActionType.CopyFiles:
                    _fileService.CopyFiles(src, dest);
                    break;
                case ActionType.MkDir:
                    _fileService.MkDir(dest);
                    break;
                case ActionType.CreateRegistry:
                    _registryService.CreateKey(
                        action.RegistryHive,
                        action.RegistryKey,
                        action.RegistryValue,
                        ExpandVariables(action.RegistryData, installDir));
                    break;
                case ActionType.CreateShortcut:
                    _shortcutService.CreateShortcut(
                        dest,
                        src,
                        action.ShortcutName,
                        ExpandVariables(action.ShortcutIcon, installDir));
                    break;
            }

            await Task.Delay(150);
        }
    }

    public static string ExpandVariables(string input, string installDir)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input
            .Replace("%INSTALLDIR%", installDir)
            .Replace("%PROGRAMFILES%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
            .Replace("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))
            .Replace("%SYSTEM%", Environment.GetFolderPath(Environment.SpecialFolder.System))
            .Replace("%DESKTOP%", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory))
            .Replace("%STARTMENU%", Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"));
    }
}
