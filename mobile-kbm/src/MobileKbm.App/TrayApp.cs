using MobileKbm.Core;
using Phonepads.Protocol;

namespace MobileKbm.App;

/// <summary>
/// The whole app: a tray icon and its menu. No window of its own — the session keeper runs
/// in the background and the menu only shows its state and offers the few things a user
/// changes: the driver key, pointer speed, starting with Windows, pause, quit.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly SynchronizationContext _ui;
    private readonly KbmSettings _settings;
    private readonly KbmController _controller;
    private readonly SessionKeeper _keeper;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _keeping;
    private readonly Task _ticking;
    private readonly TrayIcons _icons = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _status;
    private readonly ToolStripMenuItem _pause;

    private KeeperState _state;
    private Form? _openDialog;
    private bool _quitting;

    public TrayApp()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = SettingsStore.Load();
        Startup.Refresh();

        var baseUri = Uri.TryCreate(_settings.BaseUrl, UriKind.Absolute, out var configured)
            ? configured
            : new Uri(DriverClient.DefaultBaseUrl);
        var client = new DriverClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, baseUri);

        _controller = new KbmController(new WindowsInputSink(), KbmSchemas.All) { PointerSpeed = _settings.PointerSpeed };
        _keeper = new SessionKeeper(
            new ServiceDriverApi(client),
            _controller,
            () => KbmSchemas.CreateSessionConfig(Environment.MachineName));

        _status = new ToolStripMenuItem("Starting…") { Enabled = false };
        // Private sessions need no code: the owner sees them listed on the site, logged in.
        var hint = new ToolStripMenuItem(
            "On your phone: " + baseUri.Host, null, (_, _) => Browser.Open(baseUri.ToString()))
        {
            ToolTipText = "Log in with the account that owns the driver key; the session is listed there.",
        };
        _pause = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause());
        var settings = new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings());
        var quit = new ToolStripMenuItem("Quit", null, (_, _) => Quit());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _status, hint, new ToolStripSeparator(), _pause, settings, new ToolStripSeparator(), quit,
        });

        _tray = new NotifyIcon
        {
            Icon = _icons.For(TrayIcons.Busy),
            Text = "Mobile KBM",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => TogglePause();

        _keeper.StateChanged += state => _ui.Post(_ => Show(state), null);
        _keeper.SetPaused(_settings.Paused);
        _keeper.SetDriverKey(SettingsStore.ReadKey(_settings));
        _state = _keeper.State;
        Show(_state);

        _keeping = _keeper.RunAsync(_cts.Token);
        _ticking = _controller.RunTickerAsync(_cts.Token);

        // First run: nothing works without a key, so ask straight away.
        if (_settings.ProtectedDriverKey is null) _ui.Post(_ => OpenSettings(), null);
    }

    private void Show(KeeperState state)
    {
        if (_quitting) return;
        var previous = _state;
        _state = state;

        var phones = state.PhonesConnected switch
        {
            0 => "no phone connected",
            1 => "1 phone connected",
            var n => $"{n} phones connected",
        };

        var (text, color) = state.Status switch
        {
            KeeperStatus.NeedsKey => ("No driver key — set one to start", TrayIcons.Problem),
            KeeperStatus.KeyRejected => ("Driver key rejected — set a new one", TrayIcons.Problem),
            KeeperStatus.Connecting => ("Connecting…", TrayIcons.Busy),
            KeeperStatus.Reconnecting => ("Connection lost — reconnecting…", TrayIcons.Busy),
            KeeperStatus.Offline => ("Offline — " + (state.Detail ?? "retrying"), TrayIcons.Busy),
            _ when state.Paused => ("Paused — " + phones, TrayIcons.Paused),
            _ => ("Ready — " + phones, TrayIcons.Online),
        };

        _status.Text = text;
        _tray.Icon = _icons.For(color);
        // NotifyIcon.Text is capped at 127 characters (and older shells cut at 63).
        _tray.Text = Truncate("Mobile KBM — " + text, 63);
        _pause.Text = state.Paused ? "Resume" : "Pause";
        _pause.Checked = state.Paused;

        if (state.Status == KeeperStatus.KeyRejected && previous.Status != KeeperStatus.KeyRejected)
        {
            _tray.ShowBalloonTip(
                5000, "Mobile KBM", state.Detail ?? "The driver key was not accepted.", ToolTipIcon.Warning);
        }
    }

    private void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        SettingsStore.Save(_settings);
        _keeper.SetPaused(_settings.Paused);
    }

    private void OpenSettings()
    {
        if (FocusOpenDialog()) return;

        // A key saved by another Windows user or PC does not decrypt here, so it does not count.
        var hasKey = SettingsStore.ReadKey(_settings) is not null;
        var startsWithWindows = Startup.IsEnabled;
        using var form = new SettingsForm(_settings.BaseUrl, hasKey, _settings.PointerSpeed, startsWithWindows);
        _openDialog = form;
        try
        {
            if (form.ShowDialog() != DialogResult.OK) return;

            _settings.PointerSpeed = form.PointerSpeed;
            _controller.PointerSpeed = form.PointerSpeed;
            // An empty box keeps the current key, and the running session with it.
            if (form.DriverKey is { } key) SettingsStore.WriteKey(_settings, key);
            SettingsStore.Save(_settings);
            if (form.DriverKey is not null) _keeper.SetDriverKey(form.DriverKey);

            if (form.StartWithWindows != startsWithWindows && !Startup.Set(form.StartWithWindows))
            {
                _tray.ShowBalloonTip(
                    5000, "Mobile KBM", "Windows did not allow changing the startup setting.", ToolTipIcon.Warning);
            }
        }
        finally
        {
            _openDialog = null;
        }
    }

    private bool FocusOpenDialog()
    {
        if (_openDialog is null) return false;
        _openDialog.Activate();
        return true;
    }

    private async void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _tray.Visible = false;
        _openDialog?.Close();

        // Tell the phones now rather than leaving them waiting for the service to notice.
        await _keeper.EndSessionAsync();
        await _cts.CancelAsync();
        try
        {
            await Task.WhenAll(_keeping, _ticking).WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception)
        {
            // Leaving regardless.
        }

        _controller.Clear();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _icons.Dispose();
            _cts.Dispose();
        }

        base.Dispose(disposing);
    }

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";
}
