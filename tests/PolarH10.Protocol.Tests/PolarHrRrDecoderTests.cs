using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public class PolarHrRrDecoderTests
{
    [Fact]
    public void DecodeHeartRate_8Bit_ReturnsValue()
    {
        // Flags: 0x00 (8-bit, no RR), HR = 72
        byte[] data = [0x00, 72];
        Assert.Equal(72, PolarHrRrDecoder.DecodeHeartRate(data));
    }

    [Fact]
    public void DecodeHeartRate_16Bit_ReturnsValue()
    {
        // Flags: 0x01 (16-bit), HR = 300 (0x012C LE)
        byte[] data = [0x01, 0x2C, 0x01];
        Assert.Equal(300, PolarHrRrDecoder.DecodeHeartRate(data));
    }

    [Fact]
    public void DecodeRrIntervals_TwoIntervals_ReturnsMs()
    {
        // Flags: 0x10 (8-bit HR, RR present), HR = 60
        // RR1 = 1024 (1/1024 s = 1000 ms), RR2 = 512 (500 ms)
        byte[] data = [0x10, 60, 0x00, 0x04, 0x00, 0x02];

        var rr = PolarHrRrDecoder.DecodeRrIntervals(data);
        Assert.Equal(2, rr.Length);
        Assert.Equal(1000f, rr[0], 0.1f);
        Assert.Equal(500f, rr[1], 0.1f);
    }

    [Fact]
    public void DecodeRrIntervals_NoRrPresent_ReturnsEmpty()
    {
        byte[] data = [0x00, 72];
        var rr = PolarHrRrDecoder.DecodeRrIntervals(data);
        Assert.Empty(rr);
    }

    [Fact]
    public void Decode_Combined_ReturnsBothHrAndRr()
    {
        byte[] data = [0x10, 80, 0x00, 0x03]; // HR=80, RR=768 → 750ms
        var sample = PolarHrRrDecoder.Decode(data);

        Assert.Equal(80, sample.HeartRateBpm);
        Assert.Single(sample.RrIntervalsMs);
        Assert.Equal(750f, sample.RrIntervalsMs[0], 0.1f);
    }

    [Fact]
    public void DecodeHeartRate_EmptyData_ReturnsZero()
    {
        Assert.Equal(0, PolarHrRrDecoder.DecodeHeartRate(ReadOnlySpan<byte>.Empty));
    }
}
