using System.Net;
using MobileKbm.Core;
using Phonepads.Protocol;

namespace MobileKbm.Tests;

public sealed class SessionKeeperTests : IAsyncDisposable
{
    private readonly RecordingSink _sink = new();
    private readonly FakeDriverApi _api = new();
    private readonly KbmController _kbm;
    private readonly SessionKeeper _keeper;
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;

    public SessionKeeperTests()
    {
        _kbm = new KbmController(_sink, KbmSchemas.All);
        _keeper = new SessionKeeper(_api, _kbm, () => KbmSchemas.CreateSessionConfig("PC"))
        {
            Backoff = _ => TimeSpan.Zero,
        };
    }

    private void Run() => _run = _keeper.RunAsync(_cts.Token);

    private static PlayerInfo Phone(string id, bool connected = true) =>
        new(id, "Me", "#FF6B6B", Ready: false, SchemaId: "mouse", Connected: connected);

    [Fact]
    public async Task Waits_for_a_key_before_doing_anything()
    {
        Run();
        await Wait.Until(() => _keeper.State.Status == KeeperStatus.NeedsKey);

        Assert.Empty(_api.KeysUsed);
    }

    [Fact]
    public async Task Starts_the_session_as_soon_as_it_is_there()
    {
        _keeper.SetDriverKey("gpk_test");
        Run();

        var connection = await _api.NextConnectionAsync();
        connection.RaiseSnapshot("waiting_for_players");

        Assert.Equal(["start"], connection.Commands);
        Assert.Equal(KeeperStatus.Online, _keeper.State.Status);
    }

    [Fact]
    public async Task Resumes_a_session_that_paused_while_it_was_away()
    {
        _keeper.SetDriverKey("gpk_test");
        Run();

        var connection = await _api.NextConnectionAsync();
        connection.RaiseSnapshot("paused");

        Assert.Equal(["resume"], connection.Commands);
    }

    [Fact]
    public async Task Pausing_pauses_the_session_and_resuming_resumes_it()
    {
        _keeper.SetDriverKey("gpk_test");
        Run();

        var connection = await _api.NextConnectionAsync();
        connection.RaiseSnapshot("in_progress");
        _keeper.SetPaused(true);
        connection.RaiseStateChanged("paused");
        _keeper.SetPaused(false);

        Assert.Equal(["pause", "resume"], connection.Commands);
        Assert.False(_kbm.Paused);
    }

    [Fact]
    public async Task Makes_a_new_session_when_the_old_one_ends()
    {
        _keeper.SetDriverKey("gpk_test");
        Run();

        var first = await _api.NextConnectionAsync();
        first.RaiseSnapshot("in_progress", Phone("a"));
        first.RaiseStateChanged("ended", "host_ended");

        var second = await _api.NextConnectionAsync();
        second.RaiseSnapshot("waiting_for_players");

        Assert.Equal(2, _api.KeysUsed.Count);
        Assert.Equal(["start"], second.Commands);
    }

    [Fact]
    public async Task Makes_a_new_session_when_the_token_dies()
    {
        _keeper.SetDriverKey("gpk_test");
        Run();

        var first = await _api.NextConnectionAsync();
        first.RaiseStopped("This session no longer exists.");

        await _api.NextConnectionAsync();
        Assert.Equal(2, _api.KeysUsed.Count);
    }

    [Fact]
    public async Task Retries_when_the_service_cannot_be_reached()
    {
        _api.FailNext = new SetupException("Could not reach the Gamepad service.");
        _keeper.SetDriverKey("gpk_test");
        Run();

        await _api.NextConnectionAsync();
        Assert.Equal(2, _api.KeysUsed.Count);
    }

    [Fact]
    public async Task A_refused_key_waits_for_a_new_one()
    {
        _api.FailNext = new SetupException("The driver key was not accepted.", HttpStatusCode.Unauthorized);
        _keeper.SetDriverKey("gpk_revoked");
        Run();

        await Wait.Until(() => _keeper.State.Status == KeeperStatus.KeyRejected);
        await Task.Delay(100);
        Assert.Single(_api.KeysUsed);

        _keeper.SetDriverKey("gpk_fresh");
        await _api.NextConnectionAsync();
        Assert.Equal(["gpk_revoked", "gpk_fresh"], _api.KeysUsed);
    }

    [Fact]
    public async Task A_new_key_replaces_the_running_session()
    {
        _keeper.SetDriverKey("gpk_one");
        Run();
        await _api.NextConnectionAsync();

        _keeper.SetDriverKey("gpk_two");
        await _api.NextConnectionAsync();

        Assert.Equal(["gpk_one", "gpk_two"], _api.KeysUsed);
    }

    [Fact]
    public async Task Input_and_text_reach_the_pc_and_a_drop_lets_go()
    {
        _keeper.SetDriverKey("gpk_test");
        Run();

        var connection = await _api.NextConnectionAsync();
        connection.RaiseSnapshot("in_progress", Phone("a"));
        connection.RaiseInput("a", 1, new Dictionary<string, ControlValue> { ["left"] = ControlValue.Button(true) });
        connection.RaiseText("a", "type", "hi");
        connection.RaisePlayerChanged(Phone("a", connected: false));

        Assert.Equal(["down Left", "text hi", "up Left"], _sink.Events);
        Assert.Equal(0, _keeper.State.PhonesConnected);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_run is not null) await _run;
        _cts.Dispose();
    }
}
