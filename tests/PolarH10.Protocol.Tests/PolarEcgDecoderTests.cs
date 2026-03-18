using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public class PolarEcgDecoderTests
{
    /// <summary>
    /// Verify ECG decoder with a synthetic PMD frame: 10-byte header + 3 samples × 3 bytes.
    /// </summary>
    [Fact]
    public void DecodeEcgMicroVolts_ValidFrame_ReturnsCorrectSamples()
    {
        // 10-byte header (measurement type 0x00, 8-byte timestamp, frame type 0x00)
        // + 3 ECG samples as 24-bit LE signed
        var frame = new byte[]
        {
            0x00,                                           // measurement type: ECG
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // timestamp = 1 ns
            0x00,                                           // frame type: uncompressed
            0x64, 0x00, 0x00,                               // sample 0 = 100 µV
            0x9C, 0xFF, 0xFF,                               // sample 1 = -100 µV (sign-extended)
            0x00, 0x00, 0x00,                               // sample 2 = 0 µV
        };

        var samples = PolarEcgDecoder.DecodeMicroVolts(frame);

        Assert.Equal(3, samples.Length);
        Assert.Equal(100, samples[0]);
        Assert.Equal(-100, samples[1]);
        Assert.Equal(0, samples[2]);
    }

    [Fact]
    public void ReadTimestampNs_ValidFrame_ReturnsTimestamp()
    {
        var frame = new byte[]
        {
            0x00,
            0x00, 0xCA, 0x9A, 0x3B, 0x00, 0x00, 0x00, 0x00, // 1,000,000,000 ns = 1 second
            0x00,
            0x00, 0x00, 0x00,
        };

        long ts = PolarEcgDecoder.ReadTimestampNs(frame);
        Assert.Equal(1_000_000_000L, ts);
    }

    [Fact]
    public void DecodeEcgMicroVolts_FrameTooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() => PolarEcgDecoder.DecodeMicroVolts(new byte[5]));
    }

    [Fact]
    public void DecodeEcgMicroVolts_NullFrame_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PolarEcgDecoder.DecodeMicroVolts(null!));
    }

    [Fact]
    public void DecodeEcgMicroVolts_BadPayloadLength_Throws()
    {
        // 10-byte header + 4 bytes (not divisible by 3)
        var frame = new byte[14];
        Assert.Throws<ArgumentException>(() => PolarEcgDecoder.DecodeMicroVolts(frame));
    }
}
