using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public class PolarAccDecoderTests
{
    [Fact]
    public void DecodeMilliG_UncompressedType1_ReturnsCorrectSamples()
    {
        // 10-byte header + 2 samples × 6 bytes
        var frame = new byte[]
        {
            0x02,                                           // measurement type: ACC
            0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, // timestamp
            0x01,                                           // frame type: uncompressed type 1
            // Sample 0: X=100, Y=-200, Z=1000
            0x64, 0x00,                                     // X = 100
            0x38, 0xFF,                                     // Y = -200
            0xE8, 0x03,                                     // Z = 1000
            // Sample 1: X=0, Y=0, Z=0
            0x00, 0x00,
            0x00, 0x00,
            0x00, 0x00,
        };

        var samples = PolarAccDecoder.DecodeMilliG(frame, isCompressed: false, frameTypeBase: 0x01);

        Assert.Equal(2, samples.Length);
        Assert.Equal(100, samples[0].X);
        Assert.Equal(-200, samples[0].Y);
        Assert.Equal(1000, samples[0].Z);
        Assert.Equal(0, samples[1].X);
        Assert.Equal(0, samples[1].Y);
        Assert.Equal(0, samples[1].Z);
    }

    [Fact]
    public void DecodeMilliG_FrameTooShort_Throws()
    {
        Assert.Throws<ArgumentException>(() => PolarAccDecoder.DecodeMilliG(new byte[5]));
    }

    [Fact]
    public void DecodeMilliG_NullFrame_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PolarAccDecoder.DecodeMilliG(null!));
    }

    [Fact]
    public void AccSampleMg_ToG_ConvertsCorrectly()
    {
        var sample = new AccSampleMg(1000, -500, 250);
        var (x, y, z) = sample.ToG();

        Assert.Equal(1.0f, x, 0.001f);
        Assert.Equal(-0.5f, y, 0.001f);
        Assert.Equal(0.25f, z, 0.001f);
    }
}
