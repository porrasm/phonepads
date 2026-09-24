using System.Net;
using Phonepads.Protocol;

namespace MobileKbm.Core;

/// <summary>The two calls the keeper makes to the service; a seam for tests.</summary>
public interface IDriverApi
{
    /// <summary>Creates a session with a driver key, replacing the key's previous one.</summary>
    Task<SetupResponse> CreateAsync(string driverKey, SessionConfig config, CancellationToken ct);

    ISessionConnection Connect(SetupResponse session);
}

public sealed class ServiceDriverApi(DriverClient client) : IDriverApi
{
    public Task<SetupResponse> CreateAsync(string driverKey, SessionConfig config, CancellationToken ct) =>
        // Always replace: whatever held the slot before was an earlier run of this app.
        client.CreateAsync(driverKey, config, replaceExisting: true, ct);

    public ISessionConnection Connect(SetupResponse session) =>
        new SessionConnection(session.SocketUri(client.BaseUri)
            ?? throw new SetupException("The service returned no session to connect to."));
}

public enum KeeperStatus
{
    /// <summary>No driver key yet; nothing to do until one is set.</summary>
    NeedsKey,

    /// <summary>The service refused the key (revoked, or mistyped); waiting for a new one.</summary>
    KeyRejected,

    /// <summary>Asking the service for a session.</summary>
    Connecting,

    /// <summary>In a live session: phones can join and drive the PC.</summary>
    Online,

    /// <summary>The socket dropped; reconnecting to the same session.</summary>
    Reconnecting,

    /// <summary>The service could not be reached or refused for now; retrying on a timer.</summary>
    Offline,
}

public sealed record KeeperState(
    KeeperStatus Status,
    bool Paused,
    int PhonesConnected,
    string? Detail);

/// <summary>
/// Keeps one lobby-less, private session alive for as long as the app runs: creates it with
/// the driver key, starts it at once, reconnects after drops, and when the session is gone
/// for good — ended from the website, closed after the service lost us, idle for a day —
/// simply creates the next one. Only a missing or refused key makes it stop and wait.
/// </summary>
public sealed class SessionKeeper
{
    /// <summary>
    /// The service ends a session that has had no driver socket for 3 minutes. Reconnecting
    /// for longer than this is pointless; making a new session is quicker than finding out.
    /// </summary>
    internal static TimeSpan LostAfter { get; set; } = TimeSpan.FromSeconds(150);

    internal static TimeSpan WatchdogInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>A session that ends sooner than this counts as a failure for the backoff.</summary>
    private static readonly TimeSpan ShortLived = TimeSpan.FromMinutes(1);

    private readonly IDriverApi _api;
    private readonly KbmController _controller;
    private readonly Func<SessionConfig> _config;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, bool> _phones = new(StringComparer.Ordinal);

    private string? _key;
    private bool _paused;
    private TaskCompletionSource _wake = NewWake();
    private CancellationTokenSource? _sessionCts;
    private ISessionConnection? _connection;
    private string _serviceState = string.Empty;
    private long _lastConnected;
    private KeeperStatus _status = KeeperStatus.NeedsKey;
    private string? _detail;

    public SessionKeeper(IDriverApi api, KbmController controller, Func<SessionConfig> config, TimeProvider? time = null)
    {
        _api = api;
        _controller = controller;
        _config = config;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Delay before the next attempt after <c>n</c> failures in a row. Tests shorten it.</summary>
    internal Func<int, TimeSpan> Backoff { get; set; } =
        failures => TimeSpan.FromSeconds(failures <= 0 ? 1 : Math.Min(60, Math.Pow(2, failures)));

    /// <summary>Raised on every change, on whichever thread made it.</summary>
    public event Action<KeeperState>? StateChanged;

    public KeeperState State
    {
        get
        {
            lock (_gate) return Snapshot();
        }
    }

    /// <summary>Sets or clears the driver key. Any current session is dropped and a new one made with the new key.</summary>
    public void SetDriverKey(string? key)
    {
        lock (_gate) _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        Wake(restartSession: true);
    }

    /// <summary>
    /// Pauses or resumes. Paused, the session stays up (phones stay joined and see it paused)
    /// but nothing they send reaches the PC.
    /// </summary>
    public void SetPaused(bool paused)
    {
        lock (_gate) _paused = paused;
        _controller.Paused = paused;
        Reconcile();
        Publish();
    }

    /// <summary>Ends the current session politely, so phones are told at once. For quitting.</summary>
    public async Task EndSessionAsync()
    {
        ISessionConnection? connection;
        lock (_gate) connection = _connection;
        if (connection is null) return;

        try
        {
            await connection.EndAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Best effort: the service ends it on its own a few minutes after we are gone.
        }
    }

    /// <summary>Runs until cancelled. Never throws for service or network trouble.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await KeepAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            _controller.Clear();
        }
    }

    private async Task KeepAsync(CancellationToken ct)
    {
        var failures = 0;

        while (!ct.IsCancellationRequested)
        {
            Task wake;
            string? key;
            lock (_gate)
            {
                wake = _wake.Task;
                key = _key;
            }

            if (key is null)
            {
                Publish(KeeperStatus.NeedsKey, "Set a driver key to start.");
                await WaitAsync(wake, null, ct);
                continue;
            }

            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            lock (_gate)
            {
                // The key changed while we were reading it: go round again with the new one.
                if (_wake.Task != wake) continue;
                _sessionCts = session;
            }

            TimeSpan retryAfter;
            try
            {
                Publish(KeeperStatus.Connecting, null);

                SetupResponse response;
                try
                {
                    response = await _api.CreateAsync(key, _config(), session.Token);
                }
                catch (SetupException ex) when (ex.Status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    // Retrying a refused key only hammers the service; wait for a new one.
                    Publish(KeeperStatus.KeyRejected, ex.Message);
                    await WaitAsync(wake, null, ct);
                    continue;
                }

                var started = _time.GetTimestamp();
                await RunSessionAsync(response, session.Token);
                // Sessions dying straight away (two PCs sharing a key keep replacing each
                // other, say) back off like failures; a session that lived resets the count.
                failures = _time.GetElapsedTime(started) < ShortLived ? failures + 1 : 0;
                retryAfter = Backoff(failures);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // The key changed: start over with it at once.
                failures = 0;
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unreachable service, a 5xx, a rate limit — or a bug. Whatever it is, the
                // keeper backs off and tries again; it never gives up while it has a key.
                failures++;
                retryAfter = Backoff(failures);
                Publish(KeeperStatus.Offline, $"{ex.Message} Retrying in {retryAfter.TotalSeconds:0} s.");
            }
            finally
            {
                lock (_gate) _sessionCts = null;
            }

            await WaitAsync(wake, retryAfter, ct);
        }
    }

    /// <summary>Runs one session until it is over for good. Throws when <paramref name="token"/> is cancelled.</summary>
    private async Task RunSessionAsync(SetupResponse response, CancellationToken token)
    {
        using var over = CancellationTokenSource.CreateLinkedTokenSource(token);
        var connection = _api.Connect(response);

        void End()
        {
            try
            {
                // Asynchronously: this can be called from inside the connection's own receive loop.
                _ = over.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Already over.
            }
        }

        connection.SnapshotReceived += snapshot =>
        {
            lock (_gate)
            {
                _serviceState = snapshot.State;
                _phones.Clear();
                foreach (var player in snapshot.Players) _phones[player.Id] = player.Connected;
            }

            // The snapshot is the truth: whatever was held while we were away is let go.
            _controller.Clear();
            foreach (var player in snapshot.Players) _controller.SetPlayer(player.Id, player.SchemaId, player.Connected);

            if (snapshot.State == "ended") End();
            else Reconcile();
            Publish();
        };

        connection.StateChanged += (state, _) =>
        {
            lock (_gate) _serviceState = state;
            if (state == "ended")
            {
                End();
                return;
            }

            if (state == "paused") _controller.ReleaseAll();
            Reconcile();
        };

        connection.PlayerChanged += player =>
        {
            lock (_gate) _phones[player.Id] = player.Connected;
            _controller.SetPlayer(player.Id, player.SchemaId, player.Connected);
            Publish();
        };

        connection.PlayerLeft += playerId =>
        {
            lock (_gate) _phones.Remove(playerId);
            _controller.RemovePlayer(playerId);
            Publish();
        };

        connection.InputReceived += (playerId, seq, controls) =>
            _controller.OnInput(playerId, seq, controls, _controller.Now);

        connection.TextReceived += (playerId, controlId, text) => _controller.OnText(playerId, controlId, text);

        connection.StatusChanged += status =>
        {
            if (status == ConnectionStatus.Connected)
            {
                Interlocked.Exchange(ref _lastConnected, _time.GetTimestamp());
                Publish(KeeperStatus.Online, null);
            }
            else if (status is ConnectionStatus.Reconnecting or ConnectionStatus.Connecting)
            {
                // Releases may be lost while we are away; let go now rather than risk a stuck key.
                _controller.ReleaseAll();
                Publish(status == ConnectionStatus.Connecting ? KeeperStatus.Connecting : KeeperStatus.Reconnecting, null);
            }
        };

        connection.Stopped += reason =>
        {
            lock (_gate) _detail = reason;
            End();
        };

        lock (_gate)
        {
            _connection = connection;
            _serviceState = string.Empty;
            _phones.Clear();
        }

        Interlocked.Exchange(ref _lastConnected, _time.GetTimestamp());

        try
        {
            var run = connection.RunAsync(over.Token);
            while (!run.IsCompleted && !over.IsCancellationRequested)
            {
                await Task.WhenAny(run, Task.Delay(WatchdogInterval, _time, over.Token));

                var away = _time.GetElapsedTime(Interlocked.Read(ref _lastConnected));
                if (connection.Status != ConnectionStatus.Connected && away > LostAfter) End();
            }

            try
            {
                await run;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A connection that dies of something unexpected is just a session that ended.
                lock (_gate) _detail = ex.Message;
            }
        }
        finally
        {
            lock (_gate)
            {
                _connection = null;
                _serviceState = string.Empty;
                _phones.Clear();
            }

            _controller.Clear();
            await connection.DisposeAsync();
        }

        token.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// Nudges the service toward what we want: a session that is running, or paused when the
    /// user paused. Called on every state the service reports, so it converges after drops.
    /// </summary>
    private void Reconcile()
    {
        ISessionConnection? connection;
        string state;
        bool paused;
        lock (_gate)
        {
            connection = _connection;
            state = _serviceState;
            paused = _paused;
        }

        if (connection is not { Status: ConnectionStatus.Connected }) return;

        switch (state)
        {
            // No lobby: start is accepted with nobody there. Pausing follows once it runs.
            case "waiting_for_players":
                _ = connection.StartAsync(CancellationToken.None);
                break;
            case "in_progress" when paused:
                _ = connection.PauseAsync(CancellationToken.None);
                break;
            // Also how a session auto-paused by our own dropped connection gets going again.
            case "paused" when !paused:
                _ = connection.ResumeAsync(CancellationToken.None);
                break;
        }
    }

    private void Wake(bool restartSession)
    {
        TaskCompletionSource wake;
        CancellationTokenSource? session;
        lock (_gate)
        {
            wake = _wake;
            _wake = NewWake();
            session = restartSession ? _sessionCts : null;
        }

        wake.TrySetResult();
        try
        {
            session?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That session finished on its own in the meantime.
        }
    }

    /// <summary>Waits for <paramref name="delay"/> (forever when null) or until woken, whichever is first.</summary>
    private async Task WaitAsync(Task wake, TimeSpan? delay, CancellationToken ct)
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sleep = Task.Delay(delay ?? Timeout.InfiniteTimeSpan, _time, stop.Token);
        await Task.WhenAny(wake, sleep);
        await stop.CancelAsync();
        ct.ThrowIfCancellationRequested();
    }

    private void Publish(KeeperStatus status, string? detail)
    {
        lock (_gate)
        {
            _status = status;
            _detail = detail;
        }

        Publish();
    }

    private void Publish()
    {
        KeeperState state;
        lock (_gate) state = Snapshot();
        StateChanged?.Invoke(state);
    }

    private KeeperState Snapshot() =>
        new(_status, _paused, _phones.Values.Count(connected => connected), _detail);

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
