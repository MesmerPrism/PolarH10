namespace PolarH10.Protocol;

/// <summary>
/// Decodes Polar H10 accelerometer samples from PMD data frames.
/// Supports both uncompressed and compressed delta frame formats.
/// </summary>
public static class PolarAccDecoder
{
    /// <summary>PMD header size in bytes.</summary>
    public const int PmdHeaderSize = 10;

    /// <summary>Bytes per uncompressed ACC sample (3 × 16-bit signed = 6 bytes).</summary>
    public const int BytesPerUncompressedSample = 6;

    /// <summary>
    /// Decode accelerometer samples from a PMD data frame (values in milli-g).
    /// </summary>
    /// <param name="pmdFrame">Raw PMD data frame bytes (including 10-byte header).</param>
    /// <param name="isCompressed">True if the frame uses compressed delta encoding.</param>
    /// <param name="frameTypeBase">Base frame type byte (lower 7 bits).</param>
    public static AccSampleMg[] DecodeMilliG(byte[] pmdFrame, bool isCompressed = false, byte frameTypeBase = 0x01)
    {
        ArgumentNullException.ThrowIfNull(pmdFrame);
        if (pmdFrame.Length < PmdHeaderSize)
            throw new ArgumentException("PMD frame too short", nameof(pmdFrame));

        if (!isCompressed && frameTypeBase == 0x01)
            return DecodeUncompressedType1(pmdFrame);

        return DecodeCompressedFrame(pmdFrame);
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
    /// Decode a full ACC frame (timestamp + samples) from a PMD data frame.
    /// </summary>
    public static PolarAccFrame DecodeFrame(byte[] pmdFrame, long receivedUtcTicks, bool isCompressed = false, byte frameTypeBase = 0x01)
    {
        long tsNs = ReadTimestampNs(pmdFrame);
        var samples = DecodeMilliG(pmdFrame, isCompressed, frameTypeBase);
        return new PolarAccFrame(tsNs, receivedUtcTicks, samples);
    }

    private static AccSampleMg[] DecodeUncompressedType1(byte[] pmdFrame)
    {
        int payloadLength = pmdFrame.Length - PmdHeaderSize;
        if (payloadLength % BytesPerUncompressedSample != 0)
            throw new ArgumentException("Bad ACC PMD frame length for uncompressed type 1", nameof(pmdFrame));

        int sampleCount = payloadLength / BytesPerUncompressedSample;
        var samples = new AccSampleMg[sampleCount];

        int idx = 0;
        for (int offset = PmdHeaderSize; offset < pmdFrame.Length; offset += BytesPerUncompressedSample)
        {
            short x = BitConverter.ToInt16(pmdFrame, offset);
            short y = BitConverter.ToInt16(pmdFrame, offset + 2);
            short z = BitConverter.ToInt16(pmdFrame, offset + 4);
            samples[idx++] = new AccSampleMg(x, y, z);
        }

        return samples;
    }

    private static AccSampleMg[] DecodeCompressedFrame(byte[] pmdFrame)
    {
        if (pmdFrame.Length < 16)
            throw new ArgumentException("Compressed ACC frame too short", nameof(pmdFrame));

        var samples = new List<AccSampleMg>(64);

        // Reference sample at offset 10: 3 × 16-bit signed integers
        int refX = BitConverter.ToInt16(pmdFrame, 10);
        int refY = BitConverter.ToInt16(pmdFrame, 12);
        int refZ = BitConverter.ToInt16(pmdFrame, 14);
        samples.Add(new AccSampleMg((short)refX, (short)refY, (short)refZ));

        if (pmdFrame.Length <= 16)
            return samples.ToArray();

        int bitOffset = 0;
        int byteOffset = 16;
        int remainingBytes = pmdFrame.Length - 16;

        // Typical for H10 ACC at 200 Hz with 16-bit resolution
        int deltaBitWidth = 16;

        int prevX = refX, prevY = refY, prevZ = refZ;

        int bitsPerSample = deltaBitWidth * 3;
        int totalBits = remainingBytes * 8;
        int deltaSampleCount = totalBits / bitsPerSample;

        for (int i = 0; i < deltaSampleCount; i++)
        {
            int dx = ReadSignedBits(pmdFrame, byteOffset, ref bitOffset, deltaBitWidth);
            int dy = ReadSignedBits(pmdFrame, byteOffset, ref bitOffset, deltaBitWidth);
            int dz = ReadSignedBits(pmdFrame, byteOffset, ref bitOffset, deltaBitWidth);

            prevX += dx;
            prevY += dy;
            prevZ += dz;

            samples.Add(new AccSampleMg(
                ClampToInt16(prevX),
                ClampToInt16(prevY),
                ClampToInt16(prevZ)));
        }

        return samples.ToArray();
    }

    private static short ClampToInt16(int value)
    {
        if (value > short.MaxValue) return short.MaxValue;
        if (value < short.MinValue) return short.MinValue;
        return (short)value;
    }

    private static int ReadSignedBits(byte[] data, int startByteOffset, ref int bitOffset, int bitWidth)
    {
        if (bitWidth is <= 0 or > 32)
            throw new ArgumentOutOfRangeException(nameof(bitWidth));

        int totalBitPos = bitOffset;
        int bytePos = startByteOffset + (totalBitPos / 8);
        int bitInByte = totalBitPos % 8;

        long value = 0;
        int bitsRead = 0;

        while (bitsRead < bitWidth && bytePos < data.Length)
        {
            int bitsAvailableInByte = 8 - bitInByte;
            int bitsToRead = Math.Min(bitsAvailableInByte, bitWidth - bitsRead);

            int mask = (1 << bitsToRead) - 1;
            int bits = (data[bytePos] >> bitInByte) & mask;

            value |= (long)bits << bitsRead;
            bitsRead += bitsToRead;

            bytePos++;
            bitInByte = 0;
        }

        bitOffset += bitWidth;

        // Sign-extend
        if (bitWidth < 32 && (value & (1L << (bitWidth - 1))) != 0)
        {
            value |= ~((1L << bitWidth) - 1);
        }

        return (int)value;
    }
}
