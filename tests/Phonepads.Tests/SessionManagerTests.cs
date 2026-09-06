using Phonepads.Core;
using Phonepads.Protocol;

namespace Phonepads.Tests;

public class SessionManagerTests
{
    private static readonly MappedSchema Xbox = Presets.ById("generic-gamepad")!;
    private static readonly MappedSchema Wii = Presets.ById("wii-remote")!;

    private sealed class Rig
    {
        public FakeHub XInput { get; } = new(PadBackend.XInput);
        public FakeHub Dsu { get; }
        public FakeConnection Connection { get; } = new();
        public SessionManager Session { get; }
        public List<string> Notices { get; } = [];
        public List<SessionPhase> Phases { get; } = [];
        public List<(string PlayerId, PadState State)> PadUpdates { get; } = [];
        public List<(string PlayerId, MotionSample Sample)> MotionUpdates { get; } = [];

        public Rig(bool xinputAvailable = true, bool dsuAvailable = true, params MappedSchema[] offered)
        {
            XInput = new FakeHub(PadBackend.XInput, xinputAvailable);
            Dsu = new FakeHub(PadBackend.WiiRemote, dsuAvailable);

            var hubs = new Dictionary<PadBackend, IVirtualPadHub>
            {
                [PadBackend.XInput] = XInput,
                [PadBackend.WiiRemote] = Dsu,
            };

            Session = new SessionManager(hubs, (_, _) => Connection);
            Session.Notice += Notices.Add;
            Session.PhaseChanged += Phases.Add;
            Session.PadUpdated += (id, state) => PadUpdates.Add((id, state));
            Session.MotionUpdated += (id, sample) => MotionUpdates.Add((id, sample));

            Session.Attach(
                new Uri("https://example.test"),
                new SetupResponse { Success = true, JoinCode = "XYZ789", WsPath = "/ws" },
                offered.Length == 0 ? [Xbox, Wii] : offered);
        }

        public static PlayerInfo Player(string id, string schema, bool ready = true, bool connected = true) =>
            new(id, id.ToUpperInvariant(), "#FF0000", ready, schema, connected);

        public async Task JoinAndStart(params PlayerInfo[] players)
        {
            Connection.RaiseSnapshot("waiting_for_players", players);
            await Session.StartAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void Attaching_enters_the_lobby_and_remembers_the_join_code()
    {
        var rig = new Rig();

        Assert.Equal(SessionPhase.Lobby, rig.Session.Phase);
        Assert.Equal("XYZ789", rig.Session.JoinCode);
    }

    [Fact]
    public void Players_get_slots_in_join_order_and_the_backend_of_their_schema()
    {
        var rig = new Rig();

        rig.Connection.RaiseSnapshot("waiting_for_players",
            Rig.Player("ada", "generic-gamepad"),
            Rig.Player("bob", "wii-remote"));

        var players = rig.Session.Players;
        Assert.Equal(0, players[0].Slot);
        Assert.Equal(PadBackend.XInput, players[0].Backend);
        Assert.Equal(1, players[1].Slot);
        Assert.Equal(PadBackend.WiiRemote, players[1].Backend);
    }

    [Fact]
    public void A_fifth_player_gets_no_pad()
    {
        var rig = new Rig();

        rig.Connection.RaiseSnapshot("waiting_for_players",
            Enumerable.Range(1, 5).Select(i => Rig.Player($"p{i}", "generic-gamepad")).ToArray());

        var players = rig.Session.Players;
        Assert.Equal(4, players.Count(p => p.HasPad));
        Assert.Single(players, p => !p.HasPad);
    }

    [Fact]
    public void An_unknown_schema_falls_back_to_the_first_offered()
    {
        var rig = new Rig(offered: [Wii, Xbox]);

        rig.Connection.RaiseSnapshot("waiting_for_players", Rig.Player("ada", "no-such-schema"));

        Assert.Equal(PadBackend.WiiRemote, rig.Session.Players[0].Backend);
    }

    [Fact]
    public async Task Starting_creates_each_player_a_pad_from_the_right_backend()
    {
        var rig = new Rig();

        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"), Rig.Player("bob", "wii-remote"));

        Assert.Equal(SessionPhase.Running, rig.Session.Phase);
        Assert.Equal(["start"], rig.Connection.Commands);
        Assert.NotNull(rig.XInput.At(0));
        Assert.NotNull(rig.Dsu.At(1));
        Assert.Null(rig.XInput.At(1));
        Assert.Null(rig.Dsu.At(0));
    }

    [Fact]
    public async Task A_missing_backend_is_reported_once_and_the_others_still_start()
    {
        var rig = new Rig(xinputAvailable: false);

        await rig.JoinAndStart(
            Rig.Player("ada", "generic-gamepad"),
            Rig.Player("bob", "wii-remote"),
            Rig.Player("cy", "generic-gamepad"));

        Assert.Equal(SessionPhase.Running, rig.Session.Phase);
        Assert.NotNull(rig.Dsu.At(1));
        Assert.Empty(rig.XInput.Created);
        Assert.Single(rig.Notices, n => n.Contains("XInput"));
    }

    [Fact]
    public async Task With_no_backend_for_anyone_the_game_does_not_start()
    {
        var rig = new Rig(xinputAvailable: false, dsuAvailable: false);

        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        Assert.Equal(SessionPhase.Lobby, rig.Session.Phase);
        Assert.Empty(rig.Connection.Commands);
        Assert.Contains(rig.Notices, n => n.Contains("not started"));
    }

    [Fact]
    public async Task Input_frames_reach_the_players_pad_mapped_through_their_schema()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaiseInput("ada", 1, new Dictionary<string, ControlValue>
        {
            ["a"] = ControlValue.Button(true),
            ["move"] = ControlValue.Axes(1, 0),
        });

        var pad = rig.XInput.At(0)!;
        var state = Assert.Single(pad.Updates);
        Assert.True(state.IsPressed(PadButtons.A));
        Assert.Equal(short.MaxValue, state.LeftStickX);
        Assert.Equal(("ada", state), Assert.Single(rig.PadUpdates));
    }

    [Fact]
    public async Task Stale_frames_are_dropped()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaiseInput("ada", 10, new() { ["a"] = ControlValue.Button(true) });
        rig.Connection.RaiseInput("ada", 9, new() { ["a"] = ControlValue.Button(false) });
        rig.Connection.RaiseInput("ada", 10, new() { ["a"] = ControlValue.Button(false) });

        var pad = rig.XInput.At(0)!;
        Assert.Single(pad.Updates);
        Assert.True(pad.Updates[0].IsPressed(PadButtons.A));
    }

    [Fact]
    public void Input_before_start_goes_nowhere()
    {
        var rig = new Rig();
        rig.Connection.RaiseSnapshot("waiting_for_players", Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaiseInput("ada", 1, new() { ["a"] = ControlValue.Button(true) });

        Assert.Empty(rig.XInput.Created);
        Assert.Empty(rig.PadUpdates);
    }

    [Fact]
    public async Task Motion_samples_reach_the_pad_and_the_newest_is_surfaced()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("bob", "wii-remote"));

        var first = new MotionSample(1, 0, 0, 1, 10, 0, 0);
        var second = new MotionSample(2, 0, 0, 1, 20, 0, 0);
        rig.Connection.RaiseMotion("bob", first, second);

        var pad = rig.Dsu.At(0)!;
        Assert.Equal([first, second], pad.Motion);
        Assert.Equal(("bob", second), Assert.Single(rig.MotionUpdates));
    }

    [Fact]
    public async Task Motion_while_paused_is_ignored()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("bob", "wii-remote"));
        await rig.Session.PauseAsync(CancellationToken.None);

        rig.Connection.RaiseMotion("bob", new MotionSample(1, 0, 0, 1, 10, 0, 0));

        Assert.Empty(rig.Dsu.At(0)!.Motion);
        Assert.Empty(rig.MotionUpdates);
    }

    [Fact]
    public async Task Pausing_releases_every_pad_and_resuming_continues()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"), Rig.Player("bob", "wii-remote"));

        await rig.Session.PauseAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Paused, rig.Session.Phase);
        Assert.Equal(1, rig.XInput.At(0)!.Resets);
        Assert.Equal(1, rig.Dsu.At(1)!.Resets);

        await rig.Session.ResumeAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Running, rig.Session.Phase);
        Assert.Equal(["start", "pause", "resume"], rig.Connection.Commands);
    }

    [Fact]
    public async Task Ending_removes_every_pad()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"), Rig.Player("bob", "wii-remote"));

        await rig.Session.EndAsync(CancellationToken.None);

        Assert.Equal(SessionPhase.Ended, rig.Session.Phase);
        Assert.All(rig.XInput.Created, p => Assert.True(p.Disposed));
        Assert.All(rig.Dsu.Created, p => Assert.True(p.Disposed));
        Assert.Contains("end", rig.Connection.Commands);
    }

    [Fact]
    public async Task A_dropped_player_keeps_the_slot_but_the_pad_goes_neutral()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaisePlayerChanged(Rig.Player("ada", "generic-gamepad", connected: false));

        var pad = rig.XInput.At(0)!;
        Assert.Equal(1, pad.Resets);
        Assert.False(pad.Disposed);
        Assert.Equal(0, rig.Session.Players[0].Slot);
    }

    [Fact]
    public async Task A_player_who_leaves_frees_the_slot_for_the_next_arrival()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"), Rig.Player("bob", "generic-gamepad"));

        rig.Connection.RaisePlayerLeft("ada");
        Assert.True(rig.XInput.Created[0].Disposed);

        rig.Connection.RaisePlayerChanged(Rig.Player("cy", "generic-gamepad"));

        var cy = rig.Session.Players.Single(p => p.Info.Id == "cy");
        Assert.Equal(0, cy.Slot);
    }

    [Fact]
    public async Task Switching_schema_mid_session_swaps_the_pad_to_the_other_backend()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));
        var xboxPad = rig.XInput.At(0)!;

        rig.Connection.RaisePlayerChanged(Rig.Player("ada", "wii-remote"));

        Assert.True(xboxPad.Disposed);
        Assert.NotNull(rig.Dsu.At(0));
        Assert.Equal(PadBackend.WiiRemote, rig.Session.Players[0].Backend);
    }

    [Fact]
    public void Switching_schema_in_the_lobby_creates_nothing_yet()
    {
        var rig = new Rig();
        rig.Connection.RaiseSnapshot("waiting_for_players", Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaisePlayerChanged(Rig.Player("ada", "wii-remote"));

        Assert.Empty(rig.XInput.Created);
        Assert.Empty(rig.Dsu.Created);
        Assert.Equal(PadBackend.WiiRemote, rig.Session.Players[0].Backend);
    }

    [Fact]
    public async Task A_server_side_pause_resets_pads_and_explains_a_dropped_driver()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaiseStateChanged("paused", "driver_disconnected");

        Assert.Equal(SessionPhase.Paused, rig.Session.Phase);
        Assert.Equal(1, rig.XInput.At(0)!.Resets);
        Assert.Contains(rig.Notices, n => n.Contains("auto-paused"));
    }

    [Fact]
    public async Task A_lost_driver_ends_the_session_with_a_clear_message()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaiseStateChanged("ended", "driver_lost");

        Assert.Equal(SessionPhase.Ended, rig.Session.Phase);
        Assert.True(rig.XInput.Created[0].Disposed);
        Assert.Contains(rig.Notices, n => n.Contains("away for too long"));
    }

    [Fact]
    public async Task A_terminal_close_ends_the_session()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.Connection.RaiseStopped("This session no longer exists.");

        Assert.Equal(SessionPhase.Ended, rig.Session.Phase);
        Assert.Contains("This session no longer exists.", rig.Notices);
    }

    [Fact]
    public async Task Rumble_on_a_pad_vibrates_the_matching_phone()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.XInput.At(0)!.Rumble(255, 0);

        var (playerId, ms) = Assert.Single(rig.Connection.Vibrations);
        Assert.Equal("ada", playerId);
        Assert.Equal(200, ms);
    }

    [Fact]
    public async Task Rumble_can_be_switched_off()
    {
        var rig = new Rig();
        rig.Session.RumbleEnabled = false;
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        rig.XInput.At(0)!.Rumble(255, 255);

        Assert.Empty(rig.Connection.Vibrations);
    }

    [Fact]
    public async Task Disposing_removes_pads_but_leaves_the_hubs_for_the_next_session()
    {
        var rig = new Rig();
        await rig.JoinAndStart(Rig.Player("ada", "generic-gamepad"));

        await rig.Session.DisposeAsync();

        Assert.True(rig.XInput.Created[0].Disposed);
        Assert.True(rig.Connection.Disposed);
        Assert.False(rig.XInput.Disposed);
        Assert.False(rig.Dsu.Disposed);
    }

    [Fact]
    public void Offering_an_invalid_schema_is_refused_before_any_code_is_spent()
    {
        var broken = new MappedSchema(
            new Schema { Id = "broken", Name = "Broken", Controls = [] },
            Mapping.Empty("broken"));
        var hubs = new Dictionary<PadBackend, IVirtualPadHub> { [PadBackend.XInput] = new FakeHub(PadBackend.XInput) };
        var session = new SessionManager(hubs, (_, _) => new FakeConnection());

        Assert.Throws<SetupException>(() =>
            session.Attach(new Uri("https://example.test"), new SetupResponse(), [broken]));
    }

    [Fact]
    public void Offering_more_than_four_schemas_is_refused()
    {
        var hubs = new Dictionary<PadBackend, IVirtualPadHub> { [PadBackend.XInput] = new FakeHub(PadBackend.XInput) };
        var session = new SessionManager(hubs, (_, _) => new FakeConnection());

        Assert.Throws<ArgumentException>(() =>
            session.Attach(new Uri("https://example.test"), new SetupResponse(), Presets.All.Take(5).ToList()));
    }
}
