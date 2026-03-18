namespace PolarH10.Protocol;

/// <summary>
/// Decodes Polar H10 ECG samples from PMD data frames.
/// ECG samples are 3-byte little-endian signed integers (24-bit) representing microvolts.
/// </summary>
public static class PolarEcgDecoder
{
    /// <summary>PMD header size in bytes (1 byte measurement type + 8 byte timestamp + 1 byte frame type).</summary>
    public const int PmdHeaderSize = 10;

    /// <summary>Bytes per uncompressed ECG sample.</summary>
    public const int BytesPerSample = 3;

    /// <summary>
    /// Decode ECG samples from a PMD data frame.
    /// Returns an array of microvolts values.
    /// </summary>
    /// <param name="pmdFrame">Raw PMD data frame bytes (including 10-byte header).</param>
    /// <returns>Array of ECG sample values in microvolts.</returns>
    public static int[] DecodeMicroVolts(byte[] pmdFrame)
    {
        ArgumentNullException.ThrowIfNull(pmdFrame);
        if (pmdFrame.Length < PmdHeaderSize)
            throw new ArgumentException("PMD frame too short", nameof(pmdFrame));

        int payloadLength = pmdFrame.Length - PmdHeaderSize;
        if (payloadLength % BytesPerSample != 0)
            throw new ArgumentException("Bad ECG PMD frame length", nameof(pmdFrame));

        int sampleCount = payloadLength / BytesPerSample;
        var microVolts = new int[sampleCount];

        int idx = 0;
        for (int offset = PmdHeaderSize; offset < pmdFrame.Length; offset += BytesPerSample)
        {
            int raw = pmdFrame[offset]
                     | (pmdFrame[offset + 1] << 8)
                     | (pmdFrame[offset + 2] << 16);

            // Sign-extend 24-bit to 32-bit
            if ((raw & 0x0080_0000) != 0)
                raw |= unchecked((int)0xFF00_0000);

            microVolts[idx++] = raw;
        }

        return microVolts;
    }

    /// <summary>
    /// Read the sensor timestamp (nanoseconds) from the PMD frame header.
    /// </summary>
    public static long ReadTimestampNs(byte[] pmdFrame)
    {
        ArgumentNullException.ThrowIfNull(pmdFrame);
        if (pmdFrame.Length < 9)
            throw new ArgumentException("PMD frame too short for timestamp", nameof(pmdFrame));

        return unchecked((long)BitConverter.ToUInt64(pmdFrame, 1));
    }

    /// <summary>
    /// Decode a full ECG frame (timestamp + samples) from a PMD data frame.
    /// </summary>
    public static PolarEcgFrame DecodeFrame(byte[] pmdFrame, long receivedUtcTicks)
    {
        long tsNs = ReadTimestampNs(pmdFrame);
        int[] samples = DecodeMicroVolts(pmdFrame);
        return new PolarEcgFrame(tsNs, receivedUtcTicks, samples);
    }
}
