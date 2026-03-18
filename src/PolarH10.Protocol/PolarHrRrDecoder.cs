namespace PolarH10.Protocol;

/// <summary>
/// Decodes the standard BLE Heart Rate Measurement characteristic (0x2A37) payload.
/// Works with any BLE HR sensor, not just Polar.
/// </summary>
public static class PolarHrRrDecoder
{
    /// <summary>
    /// Returns true if the heart rate value is encoded as 8-bit (single byte).
    /// </summary>
    public static bool Is8BitHrFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return true;
        return (data[0] & 0b0000_0001) == 0;
    }

    /// <summary>
    /// Returns true if the energy-expended field is present in the payload.
    /// </summary>
    public static bool HasEnergyExpended(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return false;
        return (data[0] & 0b0000_1000) != 0;
    }

    /// <summary>
    /// Returns true if one or more RR-interval values are present.
    /// </summary>
    public static bool HasRrIntervals(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return false;
        return (data[0] & 0b0001_0000) != 0;
    }

    /// <summary>
    /// Decode the heart rate value (bpm) from a Heart Rate Measurement payload.
    /// </summary>
    public static ushort DecodeHeartRate(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return 0;
        if (Is8BitHrFormat(data))
            return data[1];
        if (data.Length < 3) return 0;
        return (ushort)(data[1] | (data[2] << 8));
    }

    /// <summary>
    /// Decode RR-interval values (in milliseconds) from a Heart Rate Measurement payload.
    /// The BLE spec encodes RR intervals in 1/1024 second units.
    /// </summary>
    public static float[] DecodeRrIntervals(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2) return [];

        int idx = Is8BitHrFormat(data) ? 2 : 3;
        if (HasEnergyExpended(data))
            idx += 2;

        int count = (data.Length - idx) / 2;
        if (count <= 0) return [];

        var intervals = new float[count];
        for (int i = 0; i < count; i++)
        {
            // 1/1024 s → ms: multiply by 1000/1024 = 0.9765625
            intervals[i] = (data[idx] | (data[idx + 1] << 8)) * 0.9765625f;
            idx += 2;
        }

        return intervals;
    }

    /// <summary>
    /// Convenience method: decode both HR and RR from a single payload.
    /// </summary>
    public static HrRrSample Decode(ReadOnlySpan<byte> data)
    {
        return new HrRrSample(
            DecodeHeartRate(data),
            DecodeRrIntervals(data));
    }
}

/// <summary>
/// A decoded heart-rate and RR-interval sample.
/// </summary>
public readonly record struct HrRrSample(ushort HeartRateBpm, float[] RrIntervalsMs);
