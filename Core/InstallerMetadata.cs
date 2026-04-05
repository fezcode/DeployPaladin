namespace DeployPaladin.Core;

public enum ImageMode
{
    None,
    Solid,
    Gradient
}

public class PaneImageConfig
{
    public string Path { get; set; } = string.Empty;
    public ImageMode Mode { get; set; } = ImageMode.None;
}

public class InstallerMetadata
{
    public string AppName { get; set; } = "Deploy Paladin";
    public string Version { get; set; } = "1.0.0";
    public string Company { get; set; } = "Workhammer";
    public string Theme { get; set; } = "Windows11";
    public string AppIcon { get; set; } = string.Empty;
    public string InstallDirSuffix { get; set; } = "DeployPaladin";

    public PaneImageConfig LeftPaneImage { get; set; } = new();
    public PaneImageConfig BackgroundImage { get; set; } = new();
    public PaneImageConfig TopPaneImage { get; set; } = new();
}
