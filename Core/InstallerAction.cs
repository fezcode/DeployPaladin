namespace DeployPaladin.Core;

public enum ActionType
{
    CopyFiles,
    CopyDir,
    CreateRegistry,
    CheckRegistry,
    MkDir,
    CreateShortcut
}

public class InstallerAction
{
    public ActionType Type { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string RegistryHive { get; set; } = string.Empty;
    public string RegistryKey { get; set; } = string.Empty;
    public string RegistryValue { get; set; } = string.Empty;
    public string RegistryData { get; set; } = string.Empty;
    public string ShortcutName { get; set; } = string.Empty;
    public string ShortcutIcon { get; set; } = string.Empty;

    // --- Options for Shortcuts / Consent ---
    public bool IsSelected { get; set; } = true;
    public bool IsOptional { get; set; }
    public string Label { get; set; } = string.Empty;
}
