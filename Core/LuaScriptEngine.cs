using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.IO;

namespace DeployPaladin.Core;

public class LuaScriptEngine
{
    private readonly Script _script;
    private readonly BundleReader _bundle;
    public InstallerMetadata Metadata { get; } = new();
    public List<WizardStep> Steps { get; } = new();
    public List<InstallerAction> Actions { get; } = new();
    public List<InstallerAction> RegistryChecks { get; } = new();

    public LuaScriptEngine(BundleReader bundle)
    {
        _bundle = bundle;

        _script = new Script();

        // Register API functions
        _script.Globals["SetMetadata"] = (Action<string, string>)SetMetadata;
        _script.Globals["SetTheme"] = (Action<string>)SetTheme;
        _script.Globals["SetAppIcon"] = (Action<string>)SetAppIcon;
        _script.Globals["SetInstallDirSuffix"] = (Action<string>)SetInstallDirSuffix;
        _script.Globals["AddStep"] = (Action<string, Table>)AddStep;
        _script.Globals["CopyFiles"] = (Action<string, string>)CopyFiles;
        _script.Globals["CreateRegistry"] = (Action<string, string, string, string>)CreateRegistry;
        _script.Globals["CheckRegistry"] = (Action<string, string, string>)CheckRegistry;
        _script.Globals["MkDir"] = (Action<string>)MkDir;
        _script.Globals["CreateShortcut"] = (Action<string, string, string, Table?>)CreateShortcut;

        // Image API
        _script.Globals["SetLeftPaneImage"] = (Action<string, string>)((path, mode) => SetImage(Metadata.LeftPaneImage, path, mode));
        _script.Globals["SetBackgroundImage"] = (Action<string, string>)((path, mode) => SetImage(Metadata.BackgroundImage, path, mode));
        _script.Globals["SetTopPaneImage"] = (Action<string, string>)((path, mode) => SetImage(Metadata.TopPaneImage, path, mode));

        // Predefined variables
        _script.Globals["PROGRAMFILES"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        _script.Globals["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _script.Globals["DESKTOP"] = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        _script.Globals["SYSTEM"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
        _script.Globals["STARTMENU"] = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
    }

    private void SetMetadata(string key, string value)
    {
        switch (key.ToLower())
        {
            case "appname": Metadata.AppName = value; break;
            case "version": Metadata.Version = value; break;
            case "company": Metadata.Company = value; break;
        }
    }

    private void SetTheme(string theme) => Metadata.Theme = theme;
    private void SetAppIcon(string iconPath) => Metadata.AppIcon = iconPath;
    private void SetInstallDirSuffix(string suffix) => Metadata.InstallDirSuffix = suffix;

    private static void SetImage(PaneImageConfig config, string path, string mode)
    {
        config.Path = path;
        config.Mode = mode.ToLower() switch
        {
            "solid" => ImageMode.Solid,
            "gradient" => ImageMode.Gradient,
            _ => ImageMode.Solid
        };
    }

    private void AddStep(string typeStr, Table config)
    {
        if (!Enum.TryParse<StepType>(typeStr, true, out var type))
            return;

        var step = new WizardStep
        {
            Id = config.Get("id").String ?? typeStr,
            Type = type,
            Title = config.Get("title").String ?? typeStr,
            Description = config.Get("description").String ?? "",
            RequireScroll = config.Get("requireScroll").Boolean
        };

        var contentFile = config.Get("contentFile").String;
        if (!string.IsNullOrEmpty(contentFile))
        {
            step.ContentFile = contentFile;
            if (_bundle.HasBundle)
            {
                step.ContentText = _bundle.ReadTextFile(contentFile);
            }
            else
            {
                string fallbackPath = Path.Combine(AppContext.BaseDirectory, contentFile);
                if (File.Exists(fallbackPath))
                    step.ContentText = File.ReadAllText(fallbackPath);
            }
        }

        Steps.Add(step);
    }

    private void CopyFiles(string src, string dest) =>
        Actions.Add(new InstallerAction { Type = ActionType.CopyFiles, Source = src, Destination = dest });

    private void CreateRegistry(string hive, string key, string value, string data) =>
        Actions.Add(new InstallerAction
        {
            Type = ActionType.CreateRegistry,
            RegistryHive = hive,
            RegistryKey = key,
            RegistryValue = value,
            RegistryData = data
        });

    private void CheckRegistry(string hive, string key, string value) =>
        RegistryChecks.Add(new InstallerAction
        {
            Type = ActionType.CheckRegistry,
            RegistryHive = hive,
            RegistryKey = key,
            RegistryValue = value
        });

    private void MkDir(string path) =>
        Actions.Add(new InstallerAction { Type = ActionType.MkDir, Destination = path });

    /// <summary>
    /// CreateShortcut(targetExe, shortcutLocation, name, optionsTable?)
    /// optionsTable: { label = "Create Desktop Shortcut", isOptional = true, isSelected = true }
    /// </summary>
    private void CreateShortcut(string targetExe, string shortcutLocation, string name, Table? options = null)
    {
        var action = new InstallerAction
        {
            Type = ActionType.CreateShortcut,
            Source = targetExe,
            Destination = shortcutLocation,
            ShortcutName = name
        };

        if (options != null)
        {
            action.Label = options.Get("label").String ?? name;
            action.IsOptional = options.Get("isOptional").Boolean;
            action.ShortcutIcon = options.Get("icon").String ?? "";
            
            var isSelected = options.Get("isSelected");
            action.IsSelected = isSelected.Type == DataType.Boolean ? isSelected.Boolean : true;
        }

        Actions.Add(action);
    }

    public void RunScript(string scriptName)
    {
        if (_bundle.HasBundle)
        {
            string scriptContent = _bundle.ReadTextFile(scriptName);
            if (!string.IsNullOrEmpty(scriptContent))
            {
                _script.DoString(scriptContent);
                return;
            }
        }

        string localPath = Path.IsPathRooted(scriptName)
            ? scriptName
            : Path.Combine(AppContext.BaseDirectory, scriptName);

        if (File.Exists(localPath))
        {
            _script.DoFile(localPath);
        }
    }

    /// <summary>
    /// Resolves an image path from the bundle or filesystem. Returns null if not found.
    /// </summary>
    public byte[]? ResolveImageBytes(string imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return null;

        if (_bundle.HasBundle)
        {
            string text = _bundle.ReadTextFile(imagePath);
            if (!string.IsNullOrEmpty(text))
            {
                // ReadTextFile returns string — we need binary. Use ExtractFile to temp.
                string tempPath = Path.Combine(Path.GetTempPath(), $"dp_{Path.GetFileName(imagePath)}");
                try
                {
                    _bundle.ExtractFile(imagePath, tempPath);
                    return File.ReadAllBytes(tempPath);
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
        }

        string localPath = Path.IsPathRooted(imagePath)
            ? imagePath
            : Path.Combine(AppContext.BaseDirectory, imagePath);

        if (File.Exists(localPath))
            return File.ReadAllBytes(localPath);

        return null;
    }
}
