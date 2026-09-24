using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Phonepads.App.ViewModels;
using Phonepads.App.Views;

namespace Phonepads.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow { DataContext = viewModel };
            viewModel.PickFolder = start => PickFolderAsync(window, start);
            desktop.MainWindow = window;

            // Ending the session on the way out is what stops orphaned pads (PLAY-5).
            desktop.ShutdownRequested += (_, _) =>
                viewModel.ShutdownAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task<string?> PickFolderAsync(Window window, string? startAt)
    {
        var storage = window.StorageProvider;
        if (!storage.CanPickFolder) return null;

        IStorageFolder? start = null;
        if (startAt is not null)
        {
            try
            {
                start = await storage.TryGetFolderFromPathAsync(startAt);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // A remembered folder that no longer exists just means no start location.
            }
        }

        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose your Dolphin folder (Documents\\Dolphin Emulator, or the folder with Dolphin.exe)",
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        return picked.FirstOrDefault()?.TryGetLocalPath();
    }
}
