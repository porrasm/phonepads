using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Phonepads.App.Services;
using Phonepads.Core;
using Phonepads.Dsu;
using Phonepads.Protocol;
using Phonepads.VirtualPads;

namespace Phonepads.App.ViewModels;

/// <summary>
/// The whole MVP flow in one screen: pick schemas, claim a session with a setup code, watch
/// the lobby fill up, start, and see the pads move.
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyDictionary<PadBackend, IVirtualPadHub> _hubs;
    private readonly Dictionary<string, PlayerRowViewModel> _rows = new(StringComparer.Ordinal);

    private SessionManager? _session;

    public MainViewModel() : this(PortableStorage.Load())
    {
    }

    private MainViewModel(AppSettings settings)
        : this(settings, new Dictionary<PadBackend, IVirtualPadHub>
        {
            [PadBackend.XInput] = ViGEmPadHub.Detect(),
            [PadBackend.WiiRemote] = DsuPadHub.Start(settings.DsuPort),
        })
    {
    }

    public MainViewModel(AppSettings settings, IReadOnlyDictionary<PadBackend, IVirtualPadHub> hubs)
    {
        _settings = settings;
        _hubs = hubs;

        GameName = settings.GameName ?? string.Empty;
        BaseUrl = settings.BaseUrl;
        RumbleEnabled = settings.RumbleEnabled;

        foreach (var preset in Presets.All)
        {
            var option = new SchemaOptionViewModel(preset);
            option.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SchemaOptionViewModel.IsSelected)) OnSelectionChanged();
            };
            SchemaOptions.Add(option);
        }

        // Restore the last choice, or fall back to the generic pad so the app is usable at once.
        var remembered = SchemaOptions.Where(o => settings.LastSchemaIds.Contains(o.Mapped.Schema.Id)).ToList();
        foreach (var option in remembered.Count > 0 ? remembered : [SchemaOptions[0]])
            option.IsSelected = true;

        var xinput = hubs[PadBackend.XInput];
        DriverAvailable = xinput.IsAvailable;
        DriverMessage = xinput.IsAvailable
            ? "ViGEmBus driver found."
            : xinput.UnavailableReason ?? "No controller driver found.";

        var wii = hubs[PadBackend.WiiRemote];
        WiiAvailable = wii.IsAvailable;
        WiiMessage = wii.IsAvailable
            ? $"DSU server listening on {(wii as DsuPadHub)?.EndPoint ?? "127.0.0.1:" + settings.DsuPort}."
            : wii.UnavailableReason ?? "Wii Remote mode is unavailable.";
    }

    // ---- Setup ----

    public ObservableCollection<SchemaOptionViewModel> SchemaOptions { get; } = [];

    [ObservableProperty]
    public partial string SetupCode { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GameName { get; set; }

    [ObservableProperty]
    public partial string BaseUrl { get; set; }

    [ObservableProperty]
    public partial int MinPlayers { get; set; } = 1;

    [ObservableProperty]
    public partial int MaxPlayers { get; set; } = 4;

    [ObservableProperty]
    public partial bool RumbleEnabled { get; set; }

    [ObservableProperty]
    public partial bool DriverAvailable { get; set; }

    [ObservableProperty]
    public partial string DriverMessage { get; set; }

    [ObservableProperty]
    public partial bool WiiAvailable { get; set; }

    [ObservableProperty]
    public partial string WiiMessage { get; set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Ready.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string DolphinInstructions =>
        $"In Dolphin: Controllers → Alternate Input Sources → enable the DSU client and add " +
        $"{(_hubs[PadBackend.WiiRemote] as DsuPadHub)?.EndPoint ?? "127.0.0.1:" + _settings.DsuPort} " +
        $"with the description \"{DolphinProfile.ServerDescription}\". Then set each Wii Remote to " +
        "Emulated and load the matching Phonepads profile — the button below writes them.";

    public string SelectionSummary
    {
        get
        {
            var chosen = SchemaOptions.Where(o => o.IsSelected).ToList();
            var count = chosen.Count;
            var text = count switch
            {
                0 => "Choose at least one layout.",
                > 4 => "Choose at most four layouts.",
                1 => "1 layout offered.",
                _ => $"{count} layouts offered — players pick on their phones.",
            };

            if (chosen.Any(o => o.IsWiiRemote) && !WiiAvailable)
                text += " Wii Remote layouts are selected but Wii Remote mode is unavailable.";
            if (chosen.Any(o => !o.IsWiiRemote) && !DriverAvailable)
                text += " Xbox layouts are selected but the ViGEmBus driver is missing.";

            return text;
        }
    }

    // ---- Session ----

    public ObservableCollection<PlayerRowViewModel> Players { get; } = [];

    [ObservableProperty]
    public partial SessionPhase Phase { get; set; } = SessionPhase.Idle;

    [ObservableProperty]
    public partial string? JoinCode { get; set; }

    [ObservableProperty]
    public partial string? JoinUrl { get; set; }

    [ObservableProperty]
    public partial Bitmap? JoinQr { get; set; }

    [ObservableProperty]
    public partial string ConnectionText { get; set; } = string.Empty;

    public bool IsSetupVisible => Phase is SessionPhase.Idle or SessionPhase.Claiming or SessionPhase.Ended;

    public bool IsSessionVisible => Phase is SessionPhase.Lobby or SessionPhase.Running or SessionPhase.Paused;

    public bool CanStart => Phase == SessionPhase.Lobby && (DriverAvailable || WiiAvailable);

    public bool CanPause => Phase == SessionPhase.Running;

    public bool CanResume => Phase == SessionPhase.Paused;

    public string ReadySummary
    {
        get
        {
            var ready = Players.Count(p => p.Ready);
            return $"{ready} of {Players.Count} ready";
        }
    }

    // ---- Commands ----

    [RelayCommand]
    private async Task ClaimAsync()
    {
        ErrorMessage = null;

        var chosen = SchemaOptions.Where(o => o.IsSelected).Select(o => o.Mapped).ToList();
        if (chosen.Count is < 1 or > 4)
        {
            ErrorMessage = "Offer between one and four layouts.";
            return;
        }

        if (string.IsNullOrWhiteSpace(SetupCode))
        {
            ErrorMessage = "Enter the setup code from the website.";
            return;
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri))
        {
            ErrorMessage = "The service address is not a valid URL.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Claiming the session…";

        try
        {
            var session = new SessionManager(_hubs) { RumbleEnabled = RumbleEnabled };
            Subscribe(session);

            var client = new DriverClient(new System.Net.Http.HttpClient(), baseUri);
            var response = await session.ClaimAsync(
                client, SetupCode, chosen, GameName, MinPlayers, MaxPlayers, CancellationToken.None);

            _session = session;
            JoinCode = response.JoinCode;
            JoinUrl = response.JoinUrl;
            JoinQr = response.JoinUrl is null ? null : QrRenderer.Render(response.JoinUrl);
            StatusMessage = "Session claimed. Share the join code.";

            Remember(response.DriverToken, chosen);
        }
        catch (SetupException ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Ready.";
        }
        catch (Exception ex)
        {
            ErrorMessage = "Something went wrong claiming the session: " + ex.Message;
            StatusMessage = "Ready.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (_session is null) return;
        await _session.StartAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        if (_session is null) return;
        await _session.PauseAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task ResumeAsync()
    {
        if (_session is null) return;
        await _session.ResumeAsync(CancellationToken.None);
    }

    [RelayCommand]
    private async Task EndAsync()
    {
        if (_session is null) return;

        await _session.EndAsync(CancellationToken.None);
        await _session.DisposeAsync();
        _session = null;

        Players.Clear();
        _rows.Clear();
        JoinCode = null;
        JoinUrl = null;
        JoinQr = null;
        Phase = SessionPhase.Idle;
        SetupCode = string.Empty;
        StatusMessage = "Session ended. All pads removed.";
    }

    [RelayCommand]
    private void OpenCreatePage() => OpenBrowser(BaseUrl);

    [RelayCommand]
    private void OpenDriverDownload() => OpenBrowser(ViGEmPadHub.DownloadUrl);

    [RelayCommand]
    private void OpenJoinUrl()
    {
        if (JoinUrl is not null) OpenBrowser(JoinUrl);
    }

    /// <summary>
    /// Writes the Dolphin profiles beside the executable and, when a Dolphin install is found
    /// in the usual place, straight into its profile folder as well.
    /// </summary>
    [RelayCommand]
    private void WriteDolphinProfiles()
    {
        try
        {
            var local = Path.Combine(PortableStorage.Root, "dolphin", "Wiimote");
            DolphinProfile.WriteAll(local);
            var targets = new List<string> { local };

            foreach (var dolphinDir in DolphinProfile.FindDolphinProfileDirectories())
            {
                DolphinProfile.WriteAll(dolphinDir);
                targets.Add(dolphinDir);
            }

            StatusMessage = "Dolphin profiles written to: " + string.Join("  |  ", targets);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = "Could not write the Dolphin profiles: " + ex.Message;
        }
    }

    // ---- Wiring ----

    private void Subscribe(SessionManager session)
    {
        session.PhaseChanged += phase => OnUiThread(() => Phase = phase);

        session.PlayersChanged += players => OnUiThread(() => SyncPlayers(players));

        session.ConnectionChanged += status => OnUiThread(() => ConnectionText = status switch
        {
            ConnectionStatus.Connecting => "Connecting…",
            ConnectionStatus.Connected => "Connected",
            ConnectionStatus.Reconnecting => "Reconnecting…",
            ConnectionStatus.Closed => "Disconnected",
            _ => string.Empty,
        });

        session.Notice += message => OnUiThread(() => StatusMessage = message);

        session.PadUpdated += (playerId, state) => OnUiThread(() =>
        {
            if (_rows.TryGetValue(playerId, out var row)) row.ShowPad(state);
        });

        session.MotionUpdated += (playerId, sample) => OnUiThread(() =>
        {
            if (_rows.TryGetValue(playerId, out var row)) row.ShowMotion(sample);
        });
    }

    private void SyncPlayers(IReadOnlyList<SessionPlayer> players)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var player in players)
        {
            seen.Add(player.Info.Id);
            var schemaName = SchemaOptions
                .FirstOrDefault(o => o.Mapped.Schema.Id == player.Info.SchemaId)?.Name
                ?? player.Info.SchemaId ?? "—";

            if (_rows.TryGetValue(player.Info.Id, out var row))
            {
                row.Update(player, schemaName);
            }
            else
            {
                row = new PlayerRowViewModel(player) { SchemaName = schemaName };
                _rows[player.Info.Id] = row;
                Players.Add(row);
            }
        }

        foreach (var gone in _rows.Keys.Where(id => !seen.Contains(id)).ToList())
        {
            if (_rows.Remove(gone, out var row)) Players.Remove(row);
        }

        OnPropertyChanged(nameof(ReadySummary));
    }

    private void Remember(string? driverToken, IReadOnlyList<MappedSchema> chosen)
    {
        _settings.GameName = GameName;
        _settings.BaseUrl = BaseUrl;
        _settings.DriverToken = driverToken;
        _settings.RumbleEnabled = RumbleEnabled;
        _settings.LastSchemaIds = chosen.Select(c => c.Schema.Id).ToList();
        PortableStorage.Save(_settings);
    }

    private void OnSelectionChanged() => OnPropertyChanged(nameof(SelectionSummary));

    partial void OnPhaseChanged(SessionPhase value)
    {
        OnPropertyChanged(nameof(IsSetupVisible));
        OnPropertyChanged(nameof(IsSessionVisible));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
    }

    partial void OnDriverAvailableChanged(bool value) => OnPropertyChanged(nameof(CanStart));

    partial void OnWiiAvailableChanged(bool value) => OnPropertyChanged(nameof(CanStart));

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorMessage = "Could not open the browser: " + ex.Message;
        }
    }

    /// <summary>Ends the session on shutdown so no virtual pads outlive the app (PLAY-5).</summary>
    public async ValueTask ShutdownAsync()
    {
        if (_session is not null)
        {
            try
            {
                await _session.EndAsync(CancellationToken.None);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Best effort — disposing below still removes the pads.
            }

            await _session.DisposeAsync();
            _session = null;
        }

        foreach (var hub in _hubs.Values) hub.Dispose();
    }
}
