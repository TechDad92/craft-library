using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftLibrary.App.Services;
using CraftLibrary.Core;

namespace CraftLibrary.App.ViewModels;

/// <summary>
/// New versions: a quiet look when the app opens (if she wants it), a "Check now" button in Settings,
/// and a small note that asks before anything is downloaded or installed.
/// </summary>
public sealed partial class MainViewModel
{
    public static Version AppVersion { get; } = Updates.Normal(typeof(MainViewModel).Assembly.GetName().Version ?? new Version(0, 0, 0));
    public static string AppVersionText { get; } = $"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build}";

    private UpdateService? _updater;
    private UpdateService Updater => _updater ??= new UpdateService(Path.GetDirectoryName(_dataFile)!);
    private UpdateInfo? _update;
    private CancellationTokenSource? _download;

    /// <summary>The small note in the bottom corner.</summary>
    [ObservableProperty] private bool _updateOpen;
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private double _updateProgress;
    [ObservableProperty] private string _updateMessage = "";
    [ObservableProperty] private bool _updateFailed;

    public string UpdateTitle => _update == null ? "" : "Craft Library " + V(_update.Version) + " is ready";
    public string UpdateText => "You have version " + AppVersionText + ". Would you like the new one? Your files and settings stay just as they are.";
    public bool UpdateAsking => !UpdateBusy;
    public string UpdateNowText => UpdateFailed ? "Try again" : "Update now";
    /// <summary>Once the installer is about to start, there's nothing left to stop.</summary>
    [ObservableProperty] private bool _updateCanStop = true;
    partial void OnUpdateBusyChanged(bool value) => OnPropertyChanged(nameof(UpdateAsking));
    partial void OnUpdateFailedChanged(bool value) => OnPropertyChanged(nameof(UpdateNowText));

    public bool UpdateChecking { get; private set; }
    public string UpdateCheckStatus { get; private set; } = "";

    private static string V(Version v) => $"{v.Major}.{v.Minor}.{v.Build}";

    /// <summary>On opening: wait a moment so the files appear first, then look quietly. Says nothing if there's nothing new or no internet.</summary>
    private async Task CheckOnOpenAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(4));
        Updater.CleanUp();
        if (!S.CheckUpdates) return;
        await CheckAsync(asked: false);
    }

    private async Task CheckAsync(bool asked)
    {
        if (UpdateChecking || UpdateBusy) return;
        SetCheckStatus(asked ? "Looking for a new version…" : UpdateCheckStatus, checking: true);
        try
        {
            var found = await Updater.CheckAsync();
            if (found == null)
            {
                SetCheckStatus(asked ? "You have the newest version." : "", false);
                return;
            }
            _update = found;
            SetCheckStatus(asked ? "Version " + V(found.Version) + " is ready. See the note at the bottom of the window." : "", false);
            // Don't put anything over the setup guide on a first start.
            if (!asked && GuideOpen) return;
            UpdateFailed = false;
            UpdateMessage = "";
            OnPropertyChanged(nameof(UpdateTitle));
            UpdateOpen = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            SetCheckStatus(asked ? "The app couldn’t reach the internet just now, so it couldn’t check. Please try again later." : "", false);
        }
    }

    private void SetCheckStatus(string text, bool checking)
    {
        UpdateCheckStatus = text;
        UpdateChecking = checking;
        SettingsVm.Refresh();
    }

    public ICommand CheckForUpdateNow => Cmd.Of(() => _ = CheckAsync(asked: true));

    public ICommand UpdateLater => Cmd.Of(() => UpdateOpen = false);

    public ICommand UpdateNotes => Cmd.Of(() =>
    {
        var url = _update?.NotesUrl ?? Updates.ReleasesPage;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch { ShowMessage("The web page couldn’t be opened. You can find it at " + url); }
    });

    public ICommand UpdateStop => Cmd.Of(() => _download?.Cancel());

    public ICommand UpdateNow => Cmd.Of(() => _ = UpdateNowAsync());

    private async Task UpdateNowAsync()
    {
        if (_update == null || UpdateBusy) return;
        UpdateBusy = true;
        UpdateCanStop = true;
        UpdateFailed = false;
        UpdateProgress = 0;
        UpdateMessage = "Downloading the new version…";
        _download = new CancellationTokenSource();
        var progress = new Progress<double>(p => { UpdateProgress = p * 100; UpdateMessage = "Downloading the new version… " + (int)(p * 100) + "%"; });
        string? installer;
        try
        {
            installer = await Updater.DownloadAsync(_update, progress, _download.Token);
        }
        catch (OperationCanceledException) when (_download.IsCancellationRequested)
        {
            Failed("The download was stopped. Nothing has changed.");
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            Failed("The download didn’t finish (the internet may have dropped out). Nothing has changed. You can try again.");
            return;
        }
        finally { _download.Dispose(); _download = null; }

        if (installer == null)
        {
            Failed("The downloaded file wasn’t quite right, so it wasn’t used. Nothing has changed. You can try again.");
            return;
        }

        UpdateCanStop = false;
        UpdateMessage = "Craft Library will now close, put the new version in, and open again by itself.";
        UpdateProgress = 100;
        await Task.Delay(TimeSpan.FromSeconds(2));
        try
        {
            SaveNow();
            UpdateService.RunInstaller(installer);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            UpdateCanStop = true;
            Failed("The new version couldn’t be started. Nothing has changed.");
            return;
        }
        Application.Current.Shutdown();
    }

    private void Failed(string message)
    {
        UpdateBusy = false;
        UpdateFailed = true;
        UpdateMessage = message;
    }
}
