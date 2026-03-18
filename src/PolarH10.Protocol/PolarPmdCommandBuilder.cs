namespace PolarH10.Protocol;

/// <summary>
/// Builds PMD control-point command byte arrays for the Polar H10.
/// Commands are written to the PMD Control Point characteristic.
/// </summary>
public static class PolarPmdCommandBuilder
{
    // ── PMD command opcodes ───────────────────────────────────────────
    private const byte OpcodeGetSettings = 0x01;
    private const byte OpcodeStartStream = 0x02;
    private const byte OpcodeStopStream = 0x03;

    /// <summary>
    /// Build a "get measurement settings" request for the given measurement type.
    /// The device responds on the PMD Control Point with available settings.
    /// </summary>
    public static byte[] BuildGetSettingsRequest(byte measurementType)
    {
        return [OpcodeGetSettings, measurementType];
    }

    /// <summary>
    /// Build a "start ECG stream" request.
    /// </summary>
    public static byte[] BuildStartEcgRequest(int sampleRate = 130, int resolution = 14)
    {
        return BuildStartRequest(
            PolarGattIds.MeasurementTypeEcg,
            sampleRate: sampleRate,
            resolution: resolution,
            rangeG: null,
            channels: null);
    }

    /// <summary>
    /// Build a "start ACC stream" request.
    /// </summary>
    public static byte[] BuildStartAccRequest(int sampleRate = 200, int resolution = 16, int rangeG = 8)
    {
        return BuildStartAccRequestInternal(sampleRate, resolution, rangeG);
    }

    /// <summary>
    /// Build a "stop stream" request for the given measurement type.
    /// </summary>
    public static byte[] BuildStopRequest(byte measurementType)
    {
        return [OpcodeStopStream, measurementType];
    }

    /// <summary>
    /// Build a generic PMD start request for a given measurement type with optional settings.
    /// </summary>
    public static byte[] BuildStartRequest(
        byte measurementType,
        int sampleRate,
        int resolution,
        int? rangeG,
        int? channels)
    {
        var req = new List<byte>(20)
        {
            OpcodeStartStream,
            measurementType,

            // Sample rate
            PolarGattIds.SettingTypeSampleRate,
            0x01,
            (byte)(sampleRate & 0xFF),
            (byte)((sampleRate >> 8) & 0xFF),

            // Resolution
            PolarGattIds.SettingTypeResolution,
            0x01,
            (byte)(resolution & 0xFF),
            (byte)((resolution >> 8) & 0xFF)
        };

        if (rangeG.HasValue)
        {
            req.Add(PolarGattIds.SettingTypeRange);
            req.Add(0x01);
            req.Add((byte)(rangeG.Value & 0xFF));
            req.Add((byte)((rangeG.Value >> 8) & 0xFF));
        }

        if (channels.HasValue)
        {
            req.Add(PolarGattIds.SettingTypeChannels);
            req.Add(0x01);
            req.Add((byte)channels.Value);
        }

        return req.ToArray();
    }

    /// <summary>
    /// Build an ACC start request matching the Polar H10's expected TLV field order
    /// (range, sample rate, resolution).
    /// </summary>
    private static byte[] BuildStartAccRequestInternal(int sampleRate, int resolution, int rangeG)
    {
        var req = new List<byte>(16)
        {
            OpcodeStartStream,
            PolarGattIds.MeasurementTypeAcc,

            // Range first (H10 ACC convention)
            PolarGattIds.SettingTypeRange,
            0x01,
            (byte)(rangeG & 0xFF),
            (byte)((rangeG >> 8) & 0xFF),

            // Sample rate
            PolarGattIds.SettingTypeSampleRate,
            0x01,
            (byte)(sampleRate & 0xFF),
            (byte)((sampleRate >> 8) & 0xFF),

            // Resolution
            PolarGattIds.SettingTypeResolution,
            0x01,
            (byte)(resolution & 0xFF),
            (byte)((resolution >> 8) & 0xFF),
        };

        return req.ToArray();
    }
}
