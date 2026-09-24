using System.Text.Json;
using Phonepads.Protocol;

namespace Phonepads.Tests;

public class ControlValueTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Reads_a_full_joystick_as_axes()
    {
        var value = ControlValue.FromJson(Json("""{ "x": 0.4, "y": -1 }"""));

        Assert.Equal(ControlValueKind.Axes, value.Kind);
        Assert.Equal(0.4, value.X, 6);
        Assert.Equal(-1, value.Y, 6);
    }

    [Fact]
    public void Reads_a_single_axis_joystick_with_the_other_axis_at_zero()
    {
        var value = ControlValue.FromJson(Json("""{ "x": -0.2 }"""));

        Assert.Equal(ControlValueKind.Axes, value.Kind);
        Assert.Equal(-0.2, value.X, 6);
        Assert.Equal(0, value.Y);
    }

    [Fact]
    public void Reads_a_button_as_a_boolean()
    {
        Assert.True(ControlValue.FromJson(Json("true")).Pressed);
        Assert.False(ControlValue.FromJson(Json("false")).Pressed);
        Assert.Equal(ControlValueKind.Button, ControlValue.FromJson(Json("true")).Kind);
    }

    [Theory]
    [InlineData("\"c\"", DpadDirection.Center)]
    [InlineData("\"u\"", DpadDirection.Up)]
    [InlineData("\"ur\"", DpadDirection.UpRight)]
    [InlineData("\"r\"", DpadDirection.Right)]
    [InlineData("\"dr\"", DpadDirection.DownRight)]
    [InlineData("\"d\"", DpadDirection.Down)]
    [InlineData("\"dl\"", DpadDirection.DownLeft)]
    [InlineData("\"l\"", DpadDirection.Left)]
    [InlineData("\"ul\"", DpadDirection.UpLeft)]
    public void Reads_every_dpad_code(string json, DpadDirection expected)
    {
        var value = ControlValue.FromJson(Json(json));

        Assert.Equal(ControlValueKind.Dpad, value.Kind);
        Assert.Equal(expected, value.Dpad);
    }

    [Fact]
    public void An_unknown_dpad_code_is_treated_as_centred()
    {
        Assert.Equal(DpadDirection.Center, ControlValue.ParseDpad("nonsense"));
    }

    [Fact]
    public void Dpad_vectors_are_unit_length_and_use_screen_coordinates()
    {
        var (_, up) = ControlValue.DpadToVector(DpadDirection.Up);
        var (right, _) = ControlValue.DpadToVector(DpadDirection.Right);
        var (dx, dy) = ControlValue.DpadToVector(DpadDirection.DownRight);

        Assert.Equal(-1, up); // up the screen is negative y
        Assert.Equal(1, right);
        Assert.Equal(1, Math.Sqrt(dx * dx + dy * dy), 6);
    }
}

public class InputFrameTests
{
    [Fact]
    public void Parses_a_frame_with_one_control_of_every_kind()
    {
        const string json = """
        {
          "type": "input",
          "playerId": "p1",
          "seq": 421,
          "controls": {
            "drive": { "x": 0.4, "y": -1 },
            "aim":   { "x": -0.2 },
            "hat":   "ur",
            "fire":  true,
            "lean":  { "x": 0.1, "y": -0.3 }
          }
        }
        """;

        var message = JsonSerializer.Deserialize(json, ProtocolJson.Default.ServerMessage);

        Assert.NotNull(message);
        Assert.Equal("input", message.Type);
        Assert.Equal("p1", message.PlayerId);
        Assert.Equal(421, message.Seq);

        var controls = message.ReadControls();
        Assert.Equal(5, controls.Count);
        Assert.Equal(ControlValueKind.Axes, controls["drive"].Kind);
        Assert.Equal(ControlValueKind.Dpad, controls["hat"].Kind);
        Assert.Equal(DpadDirection.UpRight, controls["hat"].Dpad);
        Assert.True(controls["fire"].Pressed);
        Assert.Equal(0.1, controls["lean"].X, 6);
    }

    [Fact]
    public void A_frame_with_no_controls_yields_an_empty_set()
    {
        var message = JsonSerializer.Deserialize(
            """{ "type": "input", "playerId": "p1", "seq": 1 }""",
            ProtocolJson.Default.ServerMessage);

        Assert.NotNull(message);
        Assert.Empty(message.ReadControls());
    }
}

public class SnapshotTests
{
    [Fact]
    public void Reads_state_and_players()
    {
        const string json = """
        {
          "type": "snapshot",
          "snapshot": {
            "state": "waiting_for_players",
            "players": [
              { "id": "p1", "name": "Ada",  "color": "#FF6B6B", "ready": true,  "schemaId": "tank" },
              { "id": "p2", "name": "Grace","color": "#6BFF6B", "ready": false, "schemaId": "tank" }
            ]
          }
        }
        """;

        var message = JsonSerializer.Deserialize(json, ProtocolJson.Default.ServerMessage);
        var snapshot = SessionSnapshot.FromJson(message!.Snapshot);

        Assert.Equal("waiting_for_players", snapshot.State);
        Assert.Equal(2, snapshot.Players.Count);
        Assert.Equal("Ada", snapshot.Players[0].Name);
        Assert.True(snapshot.Players[0].Ready);
        Assert.False(snapshot.Players[1].Ready);
        Assert.Equal("tank", snapshot.Players[0].SchemaId);
    }

    [Fact]
    public void A_player_without_optional_fields_still_parses()
    {
        var player = PlayerInfo.FromJson(
            JsonDocument.Parse("""{ "id": "p9", "name": "Nobody" }""").RootElement);

        Assert.NotNull(player);
        Assert.Equal("p9", player.Id);
        Assert.False(player.Ready);
        Assert.True(player.Connected); // absent means present
        Assert.Null(player.SchemaId);
    }

    [Fact]
    public void A_player_without_an_id_is_rejected()
    {
        var player = PlayerInfo.FromJson(
            JsonDocument.Parse("""{ "name": "Anonymous" }""").RootElement);

        Assert.Null(player);
    }

    [Fact]
    public void A_snapshot_with_no_players_is_empty_rather_than_broken()
    {
        var snapshot = SessionSnapshot.FromJson(
            JsonDocument.Parse("""{ "state": "waiting_for_players" }""").RootElement);

        Assert.Empty(snapshot.Players);
    }
}

public class DriverCommandTests
{
    [Fact]
    public void Serialises_start_as_the_protocol_expects()
    {
        var json = JsonSerializer.Serialize(
            new DriverCommand { Type = "start" }, ProtocolJson.Default.DriverCommand);

        Assert.Equal("""{"type":"start"}""", json);
    }

    [Fact]
    public void Serialises_a_vibrate_message_for_one_player()
    {
        var json = JsonSerializer.Serialize(
            new DriverCommand
            {
                Type = "message",
                PlayerId = "p1",
                Payload = new MessagePayload { VibrateMs = 200 },
            },
            ProtocolJson.Default.DriverCommand);

        Assert.Contains("\"type\":\"message\"", json);
        Assert.Contains("\"playerId\":\"p1\"", json);
        Assert.Contains("\"vibrateMs\":200", json);
    }
}
