namespace PolarH10.Protocol;

/// <summary>
/// Parses PMD control-point notification responses from the Polar H10.
/// </summary>
public static class PolarPmdControlPointParser
{
    /// <summary>Response frame identifier for PMD settings responses.</summary>
    private const byte ResponseFrameId = 0xF0;

    /// <summary>OpCode indicating a settings response.</summary>
    private const byte OpcodeGetSettingsResponse = 0x01;

    /// <summary>OpCode indicating a start stream response.</summary>
    private const byte OpcodeStartStreamResponse = 0x02;

    /// <summary>OpCode indicating a stop stream response.</summary>
    private const byte OpcodeStopStreamResponse = 0x03;

    /// <summary>
    /// Try to parse a PMD control point response payload.
    /// </summary>
    public static bool TryParse(byte[] data, out PmdControlPointResponse response)
    {
        response = default;
        if (data == null || data.Length < 4) return false;

        byte frameId = data[0];
        byte opCode = data[1];
        byte measurementType = data[2];
        byte errorCode = data[3];

        response = new PmdControlPointResponse(frameId, opCode, measurementType, errorCode);
        return true;
    }

    /// <summary>
    /// Try to parse a settings response and extract the available PMD settings.
    /// </summary>
    public static bool TryParseSettings(byte[] data, out byte measurementType, out PmdSettings settings)
    {
        measurementType = 0;
        settings = default;

        if (data == null || data.Length < 5) return false;
        if (data[0] != ResponseFrameId) return false;
        if (data[1] != OpcodeGetSettingsResponse) return false;

        measurementType = data[2];
        byte errorCode = data[3];
        if (errorCode != 0x00) return false;

        // Try parsing from offset 4, then 5 (some firmware versions differ)
        if (TryParsePmdSettingsPayload(data, 4, out settings))
            return true;
        if (TryParsePmdSettingsPayload(data, 5, out settings))
            return true;

        return false;
    }

    private static bool TryParsePmdSettingsPayload(byte[] data, int offset, out PmdSettings settings)
    {
        settings = default;
        if (data.Length <= offset) return false;

        List<ushort>? sampleRates = null;
        List<ushort>? resolutions = null;
        List<ushort>? ranges = null;

        int i = offset;
        while (i + 1 < data.Length)
        {
            byte settingType = data[i++];
            byte count = data[i++];
            int bytesNeeded = count * 2;
            if (i + bytesNeeded > data.Length) break;

            for (int n = 0; n < count; n++)
            {
                ushort value = (ushort)(data[i] | (data[i + 1] << 8));
                i += 2;
                switch (settingType)
                {
                    case PolarGattIds.SettingTypeSampleRate:
                        (sampleRates ??= []).Add(value);
                        break;
                    case PolarGattIds.SettingTypeResolution:
                        (resolutions ??= []).Add(value);
                        break;
                    case PolarGattIds.SettingTypeRange:
                        (ranges ??= []).Add(value);
                        break;
                }
            }
        }

        settings = new PmdSettings(
            sampleRates?.ToArray() ?? [],
            resolutions?.ToArray() ?? [],
            ranges?.ToArray() ?? []);

        return settings.HasAny;
    }
}

/// <summary>
/// Parsed PMD control-point response header.
/// </summary>
public readonly record struct PmdControlPointResponse(
    byte FrameId,
    byte OpCode,
    byte MeasurementType,
    byte ErrorCode)
{
    /// <summary>True if the device reported success (error code 0x00).</summary>
    public bool IsSuccess => ErrorCode == 0x00;

    /// <summary>True if the device reported invalid MTU (error code 0x0A).</summary>
    public bool IsInvalidMtu => ErrorCode == 0x0A;
}

/// <summary>
/// Available PMD settings for a specific measurement type reported by the device.
/// </summary>
public readonly record struct PmdSettings(
    ushort[] SampleRates,
    ushort[] Resolutions,
    ushort[] Ranges)
{
    public bool HasAny =>
        (SampleRates is { Length: > 0 }) ||
        (Resolutions is { Length: > 0 }) ||
        (Ranges is { Length: > 0 });
}
