using ReactiveUI;
using DeployPaladin.Core;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DeployPaladin.ViewModels;

public class StepProgressViewModel : ViewModelBase
{
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set => this.RaiseAndSetIfChanged(ref _isActive, value);
    }

    private bool _isPast;
    public bool IsPast
    {
        get => _isPast;
        set => this.RaiseAndSetIfChanged(ref _isPast, value);
    }

    public string Title { get; }
    public int Index { get; }
    public bool IsLast { get; }

    public StepProgressViewModel(string title, int index, bool isLast)
    {
        Title = title;
        Index = index;
        IsLast = isLast;
    }
}

public class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly BundleReader _bundle;
    private readonly LuaScriptEngine _lua;
    private readonly ActionRunner _runner;

    public InstallerMetadata Metadata { get; }
    public WizardStep[] Steps { get; }
    public StepProgressViewModel[] StepProgress { get; }

    // --- Image bitmaps ---
    public Bitmap? LeftPaneBitmap { get; }
    public Bitmap? BackgroundBitmap { get; }
    public Bitmap? TopPaneBitmap { get; }
    public bool HasLeftPane => LeftPaneBitmap != null;
    public bool HasBackground => BackgroundBitmap != null;
    public bool HasTopPane => TopPaneBitmap != null;
    public bool LeftPaneIsGradient => Metadata.LeftPaneImage.Mode == ImageMode.Gradient;
    public bool BackgroundIsGradient => Metadata.BackgroundImage.Mode == ImageMode.Gradient;
    public bool TopPaneIsGradient => Metadata.TopPaneImage.Mode == ImageMode.Gradient;

    // --- Observable properties ---

    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        private set => this.RaiseAndSetIfChanged(ref _currentStepIndex, value);
    }

    private WizardStep? _currentStep;
    public WizardStep? CurrentStep
    {
        get => _currentStep;
        private set => this.RaiseAndSetIfChanged(ref _currentStep, value);
    }

    private string _selectedBaseDir = string.Empty;
    public string SelectedBaseDir
    {
        get => _selectedBaseDir;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedBaseDir, value);
            this.RaisePropertyChanged(nameof(FinalInstallDir));
            CheckInstallDir();
        }
    }

    private int _installProgress;
    public int InstallProgress
    {
        get => _installProgress;
        private set => this.RaiseAndSetIfChanged(ref _installProgress, value);
    }

    private string _installStatusText = string.Empty;
    public string InstallStatusText
    {
        get => _installStatusText;
        private set => this.RaiseAndSetIfChanged(ref _installStatusText, value);
    }

    private bool _isInstalling;
    public bool IsInstalling
    {
        get => _isInstalling;
        private set => this.RaiseAndSetIfChanged(ref _isInstalling, value);
    }

    private bool _isAlreadyInstalled;
    public bool IsAlreadyInstalled
    {
        get => _isAlreadyInstalled;
        private set => this.RaiseAndSetIfChanged(ref _isAlreadyInstalled, value);
    }

    private bool _isUninstallComplete;
    public bool IsUninstallComplete
    {
        get => _isUninstallComplete;
        private set => this.RaiseAndSetIfChanged(ref _isUninstallComplete, value);
    }

    private bool _isDirectoryNotEmpty;
    public bool IsDirectoryNotEmpty
    {
        get => _isDirectoryNotEmpty;
        private set => this.RaiseAndSetIfChanged(ref _isDirectoryNotEmpty, value);
    }

    private bool _isDirectoryInvalid;
    public bool IsDirectoryInvalid
    {
        get => _isDirectoryInvalid;
        private set => this.RaiseAndSetIfChanged(ref _isDirectoryInvalid, value);
    }

    private string _directoryWarning = string.Empty;
    public string DirectoryWarning
    {
        get => _directoryWarning;
        private set => this.RaiseAndSetIfChanged(ref _directoryWarning, value);
    }

    private bool _hasScrolledToBottom;
    public bool HasScrolledToBottom
    {
        get => _hasScrolledToBottom;
        set
        {
            this.RaiseAndSetIfChanged(ref _hasScrolledToBottom, value);
            this.RaisePropertyChanged(nameof(NextBlockedByScroll));
        }
    }

    // --- Computed properties ---

    public bool IsAqua => Metadata.Theme.Equals("Aqua", StringComparison.OrdinalIgnoreCase);
    public bool IsWin11 => !IsAqua;

    public string FinalInstallDir
    {
        get
        {
            if (string.IsNullOrEmpty(Metadata.InstallDirSuffix)) return _selectedBaseDir;

            string suffix = Metadata.InstallDirSuffix.Trim('\\', '/');
            string baseDir = _selectedBaseDir.TrimEnd('\\', '/');

            if (baseDir.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return _selectedBaseDir;

            return Path.Combine(_selectedBaseDir, suffix);
        }
    }

    public bool IsWelcomeStep => CurrentStep?.Type == StepType.Welcome;
    public bool IsLicenseStep => CurrentStep?.Type == StepType.License;
    public bool IsFolderStep => CurrentStep?.Type == StepType.Folder;
    public bool IsShortcutsStep => CurrentStep?.Type == StepType.Shortcuts;
    public bool IsInstallStep => CurrentStep?.Type == StepType.Install;
    public bool IsFinishStep => CurrentStep?.Type == StepType.Finish;
    public bool HasContentText => !string.IsNullOrEmpty(CurrentStep?.ContentText);
    public bool IsLastStep => _currentStepIndex == Steps.Length - 1;
    public bool ShowNormalNavigation => !IsAlreadyInstalled && !IsUninstallComplete;
    public bool RequiresScroll => CurrentStep?.Type == StepType.License && CurrentStep?.RequireScroll == true;
    public bool NextBlockedByScroll => RequiresScroll && !HasScrolledToBottom;
    public bool NextBlockedByFolder => IsFolderStep && IsDirectoryInvalid;
    public string NextButtonText => IsLastStep || IsUninstallComplete ? "Finish" : "Next";

    public List<InstallerAction> OptionalActions => _lua.Actions.Where(a => a.IsOptional).ToList();

    // --- Commands ---

    public ReactiveCommand<Unit, Unit> NextCommand { get; }
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ReinstallCommand { get; }
    public ReactiveCommand<Unit, Unit> UninstallCommand { get; }
    public ReactiveCommand<Unit, Unit> FinishAfterUninstallCommand { get; }

    public MainWindowViewModel()
    {
        _bundle = new BundleReader();
        _lua = new LuaScriptEngine(_bundle);
        _runner = new ActionRunner(_bundle);

        _lua.RunScript("installer.lua");

        Metadata = _lua.Metadata;
        Steps = _lua.Steps.ToArray();
        StepProgress = Steps.Select((s, i) => new StepProgressViewModel(s.Title, i + 1, i == Steps.Length - 1)).ToArray();

        // Load images
        LeftPaneBitmap = LoadBitmap(Metadata.LeftPaneImage.Path);
        BackgroundBitmap = LoadBitmap(Metadata.BackgroundImage.Path);
        TopPaneBitmap = LoadBitmap(Metadata.TopPaneImage.Path);

        _currentStep = Steps.FirstOrDefault();
        SelectedBaseDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        // Next: enabled when not installing, not blocked by scroll, not blocked by invalid dir
        var canNext = this.WhenAnyValue(
            x => x.IsInstalling,
            x => x.NextBlockedByScroll,
            x => x.NextBlockedByFolder,
            (installing, scrollBlocked, folderBlocked) => !installing && !scrollBlocked && !folderBlocked);
        NextCommand = ReactiveCommand.Create(OnNext, canNext);

        // Back: enabled when index > 0 and not on last step and not installing
        var canBack = this.WhenAnyValue(
            x => x.CurrentStepIndex,
            x => x.IsInstalling,
            (idx, installing) => idx > 0 && idx < Steps.Length - 1 && !installing);
        BackCommand = ReactiveCommand.Create(OnBack, canBack);

        ReinstallCommand = ReactiveCommand.Create(() => { IsAlreadyInstalled = false; });
        UninstallCommand = ReactiveCommand.Create(OnUninstall);
        FinishAfterUninstallCommand = ReactiveCommand.Create(() => Environment.Exit(0));

        CheckIfAlreadyInstalled();
    }

    public byte[]? ResolveIconBytes() => _lua.ResolveImageBytes(Metadata.AppIcon);

    private Bitmap? LoadBitmap(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            byte[]? bytes = _lua.ResolveImageBytes(path);
            if (bytes == null) return null;
            return new Bitmap(new MemoryStream(bytes));
        }
        catch
        {
            return null;
        }
    }

    private void RaiseStepChanged()
    {
        HasScrolledToBottom = false;
        this.RaisePropertyChanged(nameof(IsWelcomeStep));
        this.RaisePropertyChanged(nameof(IsLicenseStep));
        this.RaisePropertyChanged(nameof(IsFolderStep));
        this.RaisePropertyChanged(nameof(IsShortcutsStep));
        this.RaisePropertyChanged(nameof(IsInstallStep));
        this.RaisePropertyChanged(nameof(IsFinishStep));
        this.RaisePropertyChanged(nameof(HasContentText));
        this.RaisePropertyChanged(nameof(IsLastStep));
        this.RaisePropertyChanged(nameof(NextButtonText));
        this.RaisePropertyChanged(nameof(ShowNormalNavigation));
        this.RaisePropertyChanged(nameof(RequiresScroll));
        this.RaisePropertyChanged(nameof(NextBlockedByScroll));
        this.RaisePropertyChanged(nameof(NextBlockedByFolder));
        this.RaisePropertyChanged(nameof(OptionalActions));
    }

    private void NavigateTo(int index)
    {
        if (index < 0 || index >= Steps.Length) return;
        CurrentStepIndex = index;
        CurrentStep = Steps[index];

        for (int i = 0; i < StepProgress.Length; i++)
        {
            StepProgress[i].IsActive = (i == index);
            StepProgress[i].IsPast = (i < index);
        }

        RaiseStepChanged();
    }

    private void OnNext()
    {
        if (IsLastStep)
        {
            Environment.Exit(0);
            return;
        }

        int nextIndex = _currentStepIndex + 1;
        NavigateTo(nextIndex);

        if (CurrentStep?.Type == StepType.Install)
        {
            _ = RunInstallationAsync();
        }
    }

    private void OnBack()
    {
        if (_currentStepIndex > 0)
            NavigateTo(_currentStepIndex - 1);
    }

    private async Task RunInstallationAsync()
    {
        IsInstalling = true;
        InstallProgress = 0;
        InstallStatusText = "Preparing...";

        try
        {
            await _runner.RunActions(_lua.Actions, FinalInstallDir, (progress, status) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    InstallProgress = progress;
                    InstallStatusText = status;
                });
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallProgress = 100;
                InstallStatusText = "Complete!";
                NavigateTo(_currentStepIndex + 1);
            });
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                InstallStatusText = $"Error: {ex.Message}";
            });
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void CheckIfAlreadyInstalled()
    {
        var regService = new RegistryService();
        foreach (var check in _lua.RegistryChecks)
        {
            if (regService.KeyExists(check.RegistryHive, check.RegistryKey))
            {
                IsAlreadyInstalled = true;
                break;
            }
        }
    }

    private void OnUninstall()
    {
        var regService = new RegistryService();

        // Find the install dir from registry so we can expand variables
        string installDir = FinalInstallDir;
        var installDirAction = _lua.Actions.FirstOrDefault(a =>
            a.Type == ActionType.CreateRegistry &&
            a.RegistryValue.Equals("InstallDir", StringComparison.OrdinalIgnoreCase));

        if (installDirAction != null)
        {
            string? installedPath = regService.ReadValue(
                installDirAction.RegistryHive,
                installDirAction.RegistryKey,
                installDirAction.RegistryValue);

            if (!string.IsNullOrEmpty(installedPath))
                installDir = installedPath;
        }

        // Delete installed files
        if (Directory.Exists(installDir))
        {
            try { Directory.Delete(installDir, true); }
            catch { /* best effort */ }
        }

        // Delete shortcuts
        foreach (var action in _lua.Actions.Where(a => a.Type == ActionType.CreateShortcut))
        {
            string dest = ActionRunner.ExpandVariables(action.Destination, installDir);
            string lnkPath = dest;
            if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                lnkPath = Path.Combine(dest, action.ShortcutName + ".lnk");

            try
            {
                if (File.Exists(lnkPath))
                    File.Delete(lnkPath);

                // Clean up empty parent folder (e.g. Start Menu/Programs/CompanyName)
                string? parent = Path.GetDirectoryName(lnkPath);
                if (parent != null && Directory.Exists(parent) &&
                    !Directory.EnumerateFileSystemEntries(parent).Any())
                {
                    Directory.Delete(parent);
                }
            }
            catch { /* best effort */ }
        }

        // Delete registry keys
        foreach (var action in _lua.Actions.Where(a => a.Type == ActionType.CreateRegistry))
        {
            regService.DeleteKey(action.RegistryHive, action.RegistryKey);
        }

        IsAlreadyInstalled = false;
        IsUninstallComplete = true;
        RaiseStepChanged();
    }

    public void OnLicenseScrollChanged(double offset, double extent, double viewport)
    {
        if (RequiresScroll && !HasScrolledToBottom)
        {
            // Handle case where text is too short to scroll OR user reached bottom
            // extent > 0 ensures layout is ready
            if (extent > 0 && (extent <= (viewport + 5) || offset + viewport >= extent - 20))
            {
                HasScrolledToBottom = true;
            }
        }
    }

    private void CheckInstallDir()
    {
        string dir = FinalInstallDir;

        // Validate the path is a valid rooted path and parent exists
        try
        {
            if (string.IsNullOrWhiteSpace(dir) || !Path.IsPathRooted(dir))
            {
                IsDirectoryInvalid = true;
                DirectoryWarning = "Please enter a valid absolute path.";
                IsDirectoryNotEmpty = false;
                this.RaisePropertyChanged(nameof(NextBlockedByFolder));
                return;
            }

            string? parent = Path.GetDirectoryName(dir);
            if (parent != null && !Directory.Exists(parent))
            {
                // Check if it's a drive root like C:\
                bool isDriveRoot = Path.GetPathRoot(dir) == dir;
                if (!isDriveRoot)
                {
                    IsDirectoryInvalid = true;
                    DirectoryWarning = $"Parent directory does not exist: {parent}";
                    IsDirectoryNotEmpty = false;
                    this.RaisePropertyChanged(nameof(NextBlockedByFolder));
                    return;
                }
            }

            IsDirectoryInvalid = false;
            DirectoryWarning = string.Empty;

            IsDirectoryNotEmpty = Directory.Exists(dir) &&
                                  Directory.EnumerateFileSystemEntries(dir).Any();
        }
        catch
        {
            IsDirectoryInvalid = true;
            DirectoryWarning = "The path is invalid.";
            IsDirectoryNotEmpty = false;
        }

        this.RaisePropertyChanged(nameof(NextBlockedByFolder));
    }

    public void Dispose()
    {
        _bundle?.Dispose();
    }
}
