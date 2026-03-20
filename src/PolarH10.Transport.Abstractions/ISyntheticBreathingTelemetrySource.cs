using PolarH10.Protocol;

namespace PolarH10.Transport.Abstractions;

/// <summary>
/// Optional connection capability used by synthetic transports to surface
/// already-derived breathing telemetry without routing through ACC samples.
/// </summary>
public interface ISyntheticBreathingTelemetrySource
{
    /// <summary>Raised when synthetic breathing telemetry is received.</summary>
    event Action<PolarBreathingTelemetry> BreathingTelemetryReceived;
}
