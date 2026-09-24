using System.Text.Json;
using MobileKbm.Core;
using Phonepads.Protocol;

namespace MobileKbm.Tests;

public class SchemaTests
{
    [Fact]
    public void Every_bundled_schema_passes_the_service_rules()
    {
        var problems = KbmSchemas.All.SelectMany(s => s.Validate()).ToList();

        Assert.Empty(problems);
    }

    [Fact]
    public void Schema_ids_are_unique_and_within_the_limit()
    {
        var ids = KbmSchemas.All.Select(s => s.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.InRange(ids.Count, 1, 32);
        Assert.DoesNotContain("physical-gamepad", ids);
    }

    [Fact]
    public void Every_control_has_an_action()
    {
        foreach (var schema in KbmSchemas.All)
        {
            foreach (var control in schema.Controls)
                Assert.NotNull(schema.ActionFor(control.Dto.Id));
        }
    }

    [Fact]
    public void The_session_is_always_private_and_lobbyless()
    {
        var config = KbmSchemas.CreateSessionConfig("DESKTOP-1");

        Assert.True(config.Private);
        Assert.True(config.SkipLobby);
        Assert.Equal(KbmSchemas.DriverAppUuid, config.DriverAppUuid);
        Assert.Equal("mouse", config.Schemas![0].Id);
    }

    [Fact]
    public void Config_serialises_with_the_protocol_field_names()
    {
        var json = JsonSerializer.Serialize(
            new CreateRequest { Config = KbmSchemas.CreateSessionConfig("PC"), ReplaceExisting = true },
            ProtocolJson.Default.CreateRequest);

        Assert.Contains("\"replaceExisting\":true", json);
        Assert.Contains("\"skipLobby\":true", json);
        Assert.Contains("\"private\":true", json);
        Assert.Contains("\"type\":\"touchpad\"", json);
        Assert.Contains("\"aspect\":1.2", json);
        Assert.Contains("\"type\":\"raw\"", json);
    }

    [Fact]
    public void Touch_values_are_read_as_finger_lists()
    {
        var value = ControlValue.FromJson(JsonDocument.Parse(
            """[{ "id": 0, "x": 0.412, "y": 0.733 }, { "id": 1, "x": 0.871, "y": 0.205 }, { "x": 1 }]""").RootElement);

        Assert.Equal(ControlValueKind.Touches, value.Kind);
        Assert.Equal(new[] { new TouchPoint(0, 0.412, 0.733), new TouchPoint(1, 0.871, 0.205) }, value.Touches);
        Assert.Empty(ControlValue.FromJson(JsonDocument.Parse("[]").RootElement).Touches);
        Assert.Empty(default(ControlValue).Touches);
    }
}
