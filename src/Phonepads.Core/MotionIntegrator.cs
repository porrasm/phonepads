using Phonepads.Protocol;

namespace Phonepads.Core;

/// <summary>Angular rates and acceleration to hand a consumer that integrates on its own clock.</summary>
public readonly record struct MotionOutput(
    double AccelX,
    double AccelY,
    double AccelZ,
    double RateX,
    double RateY,
    double RateZ)
{
    public static MotionOutput AtRest => new(0, 0, 1, 0, 0, 0);
}

/// <summary>
/// Turns the phone's bursty sample stream into rates that integrate correctly downstream.
///
/// The phone batches several timestamped samples per message. Dolphin — the consumer that
/// matters — ignores DSU timestamps and integrates <c>rate × its own elapsed time</c>. Replaying
/// a burst back-to-back would let it see only the last sample and lose the rest of the angle.
/// So this accumulates the exact angle each sample contributes (rate × its own Δt) and, on each
/// emit, hands out <c>angle ÷ wall-clock interval</c>: the rate that, integrated over that
/// interval, reproduces the angle the phone actually turned through.
/// </summary>
public sealed class MotionIntegrator
{
    /// <summary>
    /// Longest gap a single sample may account for. After a stall (reconnect, app switch) the
    /// first sample must not claim seconds of rotation.
    /// </summary>
    public const double MaxSampleGapMs = 100;

    private readonly Lock _gate = new();
    private double? _lastTime;
    private double _angleX, _angleY, _angleZ;
    private MotionSample? _latest;

    public bool HasSamples
    {
        get
        {
            lock (_gate) return _latest.HasValue;
        }
    }

    public void Push(MotionSample sample)
    {
        lock (_gate)
        {
            if (_lastTime is { } last)
            {
                var deltaMs = sample.Time - last;

                // A negative delta means the phone's clock restarted; keep the sample, skip the angle.
                if (deltaMs > 0)
                {
                    var seconds = Math.Min(deltaMs, MaxSampleGapMs) / 1000d;
                    _angleX += sample.GyroX * seconds;
                    _angleY += sample.GyroY * seconds;
                    _angleZ += sample.GyroZ * seconds;
                }
            }

            _lastTime = sample.Time;
            _latest = sample;
        }
    }

    public void Push(ReadOnlySpan<MotionSample> samples)
    {
        foreach (var sample in samples) Push(sample);
    }

    /// <summary>
    /// Rates for the interval that just elapsed on the consumer's clock, plus the latest
    /// acceleration. Resets the accumulated angle. With no samples yet, reports a phone at rest.
    /// </summary>
    public MotionOutput Emit(TimeSpan wallElapsed)
    {
        lock (_gate)
        {
            if (_latest is not { } latest) return MotionOutput.AtRest;

            // Guard against a zero interval on the first tick.
            var seconds = Math.Max(wallElapsed.TotalSeconds, 0.001);

            var output = new MotionOutput(
                latest.AccelX, latest.AccelY, latest.AccelZ,
                _angleX / seconds, _angleY / seconds, _angleZ / seconds);

            _angleX = _angleY = _angleZ = 0;
            return output;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastTime = null;
            _latest = null;
            _angleX = _angleY = _angleZ = 0;
        }
    }
}
