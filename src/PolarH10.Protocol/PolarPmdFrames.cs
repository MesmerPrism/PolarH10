namespace PolarH10.Protocol;

/// <summary>
/// A decoded ECG data frame from the Polar H10 PMD stream.
/// </summary>
public readonly record struct PolarEcgFrame(
    long SensorTimestampNs,
    long ReceivedUtcTicks,
    int[] MicroVolts);

/// <summary>
/// A single accelerometer sample in milli-g (mg).
/// </summary>
public readonly record struct AccSampleMg(short X, short Y, short Z)
{
    /// <summary>Convert to fractional g values (1 g = 1000 mg).</summary>
    public (float X, float Y, float Z) ToG() => (X * 0.001f, Y * 0.001f, Z * 0.001f);
}

/// <summary>
/// A decoded accelerometer data frame from the Polar H10 PMD stream.
/// </summary>
public readonly record struct PolarAccFrame(
    long SensorTimestampNs,
    long ReceivedUtcTicks,
    AccSampleMg[] Samples);
