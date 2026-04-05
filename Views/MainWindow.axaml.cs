using Avalonia.Controls;
using Avalonia.Interactivity;
using DeployPaladin.ViewModels;

namespace DeployPaladin.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var licenseScroll = this.FindControl<ScrollViewer>("LicenseScroll");
        if (licenseScroll != null)
        {
            licenseScroll.ScrollChanged += OnLicenseScrollChanged;
        }
    }

    private void OnLicenseScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is ScrollViewer sv && DataContext is MainWindowViewModel vm)
        {
            vm.OnLicenseScrollChanged(sv.Offset.Y, sv.Extent.Height, sv.Viewport.Height);
        }
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(
                new Avalonia.Platform.Storage.FolderPickerOpenOptions
                {
                    Title = "Select Installation Directory",
                    AllowMultiple = false
                });

            if (folders != null && folders.Count > 0)
            {
                var path = folders[0].Path;
                if (path != null && path.IsAbsoluteUri)
                {
                    vm.SelectedBaseDir = path.LocalPath;
                }
            }
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
