using System.Text.Json;
using Phonepads.Core;
using Phonepads.Protocol;

namespace Phonepads.Tests;

public class MotionMessageTests
{
    [Fact]
    public void Parses_a_motion_message_with_several_samples()
    {
        const string json = """
        {
          "type": "motion",
          "playerId": "p1",
          "samples": [
            [1832.4, 0.02, -0.01, 0.99, 12.5, -3.0, 0.4],
            [1849.1, 0.03,  0.00, 0.98, 10.1, -2.8, 0.6]
          ]
        }
        """;

        var message = JsonSerializer.Deserialize(json, ProtocolJson.Default.ServerMessage);
        Assert.NotNull(message);
        Assert.Equal("motion", message.Type);

        var samples = message.ReadMotionSamples();

        Assert.Equal(2, samples.Count);
        Assert.Equal(1832.4, samples[0].Time, 6);
        Assert.Equal(0.99, samples[0].AccelZ, 6);
        Assert.Equal(12.5, samples[0].GyroX, 6);
        Assert.Equal(1849.1, samples[1].Time, 6);
        Assert.Equal(0.6, samples[1].GyroZ, 6);
    }

    [Fact]
    public void Rows_of_the_wrong_shape_are_skipped_not_fatal()
    {
        const string json = """
        {
          "type": "motion",
          "playerId": "p1",
          "samples": [
            [1, 0, 0, 1, 0, 0, 0],
            [2, 0, 0, 1],
            "garbage",
            [3, 0, 0, 1, 0, 0, "x"],
            [4, 0, 0, 1, 0, 0, 0]
          ]
        }
        """;

        var samples = JsonSerializer.Deserialize(json, ProtocolJson.Default.ServerMessage)!.ReadMotionSamples();

        Assert.Equal([1d, 4d], samples.Select(s => s.Time));
    }

    [Fact]
    public void A_message_without_samples_yields_none()
    {
        var samples = JsonSerializer.Deserialize(
            """{ "type": "motion", "playerId": "p1" }""",
            ProtocolJson.Default.ServerMessage)!.ReadMotionSamples();

        Assert.Empty(samples);
    }

    [Fact]
    public void TryFromJson_requires_exactly_seven_numbers()
    {
        Assert.True(MotionSample.TryFromJson(JsonDocument.Parse("[1,2,3,4,5,6,7]").RootElement, out var ok));
        Assert.Equal(7, ok.GyroZ);
        Assert.False(MotionSample.TryFromJson(JsonDocument.Parse("[1,2,3,4,5,6]").RootElement, out _));
        Assert.False(MotionSample.TryFromJson(JsonDocument.Parse("[1,2,3,4,5,6,7,8]").RootElement, out _));
        Assert.False(MotionSample.TryFromJson(JsonDocument.Parse("{}").RootElement, out _));
    }
}

public class MotionIntegratorTests
{
    private static MotionSample At(double timeMs, double gyroX = 0, double accelZ = 1) =>
        new(timeMs, 0, 0, accelZ, gyroX, 0, 0);

    [Fact]
    public void With_no_samples_it_reports_a_phone_at_rest()
    {
        var integrator = new MotionIntegrator();

        var output = integrator.Emit(TimeSpan.FromMilliseconds(16));

        Assert.Equal(MotionOutput.AtRest, output);
        Assert.False(integrator.HasSamples);
    }

    [Fact]
    public void The_first_sample_contributes_no_angle_because_it_has_no_interval()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(1000, gyroX: 500));

        var output = integrator.Emit(TimeSpan.FromSeconds(1));

        Assert.Equal(0, output.RateX);
        Assert.True(integrator.HasSamples);
    }

    [Fact]
    public void Steady_rotation_at_sensor_rate_emits_the_same_rate()
    {
        var integrator = new MotionIntegrator();

        // 90 °/s sampled every 1/60 s for one second.
        for (var i = 0; i <= 60; i++) integrator.Push(At(i * 1000d / 60, gyroX: 90));

        var output = integrator.Emit(TimeSpan.FromSeconds(1));

        Assert.Equal(90, output.RateX, 3);
    }

    [Fact]
    public void Bursts_preserve_the_total_angle_however_the_consumer_ticks()
    {
        var integrator = new MotionIntegrator();
        const double rate = 90;
        var totalAngle = 0d;

        // The phone sends 4 samples per message; the consumer ticks at 15 Hz, unrelated to that.
        var sampleIndex = 1;
        integrator.Push(At(0, rate));
        for (var tick = 0; tick < 15; tick++)
        {
            for (var s = 0; s < 4; s++, sampleIndex++)
                integrator.Push(At(sampleIndex * 1000d / 60, rate));

            var elapsed = TimeSpan.FromSeconds(1d / 15);
            var output = integrator.Emit(elapsed);
            totalAngle += output.RateX * elapsed.TotalSeconds;
        }

        // 60 sampled intervals of 1/60 s at 90 °/s is 90 degrees.
        Assert.Equal(90, totalAngle, 1);
    }

    [Fact]
    public void A_whole_burst_between_two_ticks_is_spread_over_the_tick()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(0, gyroX: 100));
        integrator.Push(At(50, gyroX: 100)); // 5 degrees
        integrator.Push(At(100, gyroX: 100)); // 5 degrees

        var output = integrator.Emit(TimeSpan.FromSeconds(1));

        // 10 degrees over a one-second tick is 10 °/s, not the 100 °/s the samples said.
        Assert.Equal(10, output.RateX, 6);
    }

    [Fact]
    public void Emitting_resets_the_accumulated_angle()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(0, gyroX: 100));
        integrator.Push(At(100, gyroX: 100));

        integrator.Emit(TimeSpan.FromMilliseconds(100));
        var second = integrator.Emit(TimeSpan.FromMilliseconds(100));

        Assert.Equal(0, second.RateX);
    }

    [Fact]
    public void A_long_gap_cannot_claim_seconds_of_rotation()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(0, gyroX: 100));
        integrator.Push(At(5000, gyroX: 100)); // five seconds later

        var output = integrator.Emit(TimeSpan.FromSeconds(1));

        // Capped at 100 ms of the sample's rate: 10 degrees, not 500.
        Assert.Equal(100 * MotionIntegrator.MaxSampleGapMs / 1000, output.RateX, 6);
    }

    [Fact]
    public void A_clock_reset_keeps_the_sample_but_skips_the_angle()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(5000, gyroX: 100, accelZ: 0.5));
        integrator.Push(At(10, gyroX: 100, accelZ: 0.7)); // phone restarted its clock

        var output = integrator.Emit(TimeSpan.FromSeconds(1));

        Assert.Equal(0, output.RateX);
        Assert.Equal(0.7, output.AccelZ, 6);
    }

    [Fact]
    public void Acceleration_is_always_the_latest_sample()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(0, accelZ: 0.1));
        integrator.Push(At(16, accelZ: 0.9));

        Assert.Equal(0.9, integrator.Emit(TimeSpan.FromMilliseconds(16)).AccelZ, 6);
        // Still the latest after an emit with nothing new pushed.
        Assert.Equal(0.9, integrator.Emit(TimeSpan.FromMilliseconds(16)).AccelZ, 6);
    }

    [Fact]
    public void A_zero_length_tick_does_not_divide_by_zero()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(0, gyroX: 100));
        integrator.Push(At(10, gyroX: 100));

        var output = integrator.Emit(TimeSpan.Zero);

        Assert.True(double.IsFinite(output.RateX));
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var integrator = new MotionIntegrator();
        integrator.Push(At(0, gyroX: 100));
        integrator.Push(At(10, gyroX: 100));

        integrator.Reset();

        Assert.False(integrator.HasSamples);
        Assert.Equal(MotionOutput.AtRest, integrator.Emit(TimeSpan.FromMilliseconds(16)));
    }
}
