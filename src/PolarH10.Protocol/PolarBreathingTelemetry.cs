using System.Numerics;

namespace PolarH10.Protocol;

public readonly record struct PolarBreathingTelemetry(
    bool IsTransportConnected,
    bool HasReceivedAnySample,
    bool IsCalibrating,
    bool IsCalibrated,
    bool HasTracking,
    bool HasUsefulSignal,
    bool HasXzModel,
    float CalibrationProgress01,
    float CurrentVolume01,
    PolarBreathingState CurrentState,
    float EstimatedSampleRateHz,
    float UsefulAxisRangeG,
    float LastProjectionG,
    float Volume3d01,
    float VolumeBase01,
    float VolumeXz01,
    Vector3 Axis,
    Vector3 Center,
    float BoundMin,
    float BoundMax,
    Vector2 XzAxis,
    float XzBoundMin,
    float XzBoundMax,
    long AccFrameCount,
    long AccSampleCount,
    float LastSampleAgeSeconds,
    string LastCalibrationFailureReason,
    PolarBreathingSettings Settings,
    DateTimeOffset? LastSampleReceivedAtUtc);
