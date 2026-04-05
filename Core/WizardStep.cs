namespace DeployPaladin.Core;

public enum StepType
{
    Welcome,
    License,
    Folder,
    Install,
    Finish
}

public class WizardStep
{
    public string Id { get; set; } = string.Empty;
    public StepType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ContentFile { get; set; } = string.Empty;
    public string ContentText { get; set; } = string.Empty;
    public bool RequireScroll { get; set; }
}
