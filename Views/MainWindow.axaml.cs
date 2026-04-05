using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DeployPaladin.ViewModels;
using System;

namespace DeployPaladin.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            if (!string.IsNullOrEmpty(vm.Metadata.AppIcon))
            {
                try
                {
                    byte[]? iconBytes = vm.ResolveIconBytes();
                    if (iconBytes != null)
                    {
                        using var stream = new System.IO.MemoryStream(iconBytes);
                        this.Icon = new Avalonia.Controls.WindowIcon(stream);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load custom icon: {ex.Message}");
                }
            }

            vm.PropertyChanged += (s, ev) =>
            {
                if (ev.PropertyName == nameof(MainWindowViewModel.CurrentStepIndex))
                {
                    ResetScrollToTop();
                    TriggerScrollCheck();
                }
            };
        }
    }

    private void TriggerScrollCheck()
    {
        Dispatcher.UIThread.Post(() =>
        {
            var licenseScroll = this.FindControl<ScrollViewer>("LicenseScroll");
            if (licenseScroll != null && DataContext is MainWindowViewModel vm)
            {
                vm.OnLicenseScrollChanged(licenseScroll.Offset.Y, licenseScroll.Extent.Height, licenseScroll.Viewport.Height);
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void ResetScrollToTop()
    {
        var licenseScroll = this.FindControl<ScrollViewer>("LicenseScroll");
        if (licenseScroll != null)
        {
            licenseScroll.Offset = new Avalonia.Vector(0, 0);
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var licenseScroll = this.FindControl<ScrollViewer>("LicenseScroll");
        if (licenseScroll != null)
        {
            licenseScroll.ScrollChanged += OnLicenseScrollChanged;
            // Also check initial state in case we're already at bottom (small text)
            if (DataContext is MainWindowViewModel vm)
            {
                vm.OnLicenseScrollChanged(licenseScroll.Offset.Y, licenseScroll.Extent.Height, licenseScroll.Viewport.Height);
            }
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
