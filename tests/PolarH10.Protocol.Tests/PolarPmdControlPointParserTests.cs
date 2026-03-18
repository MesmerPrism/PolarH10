using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public class PolarPmdControlPointParserTests
{
    [Fact]
    public void TryParse_ValidResponse_ReturnsTrue()
    {
        byte[] data = [0xF0, 0x01, 0x00, 0x00]; // Settings response, ECG, success
        Assert.True(PolarPmdControlPointParser.TryParse(data, out var response));
        Assert.Equal(0xF0, response.FrameId);
        Assert.Equal(0x01, response.OpCode);
        Assert.Equal(0x00, response.MeasurementType);
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void TryParse_InvalidMtu_Detected()
    {
        byte[] data = [0xF0, 0x02, 0x00, 0x0A]; // Start response, ECG, invalid MTU
        Assert.True(PolarPmdControlPointParser.TryParse(data, out var response));
        Assert.True(response.IsInvalidMtu);
    }

    [Fact]
    public void TryParseSettings_WithSampleRates_ParsesCorrectly()
    {
        byte[] data =
        [
            0xF0, 0x01, 0x00, 0x00, // header: settings response, ECG, success
            0x00, 0x01, 0x82, 0x00,  // sample rate: 1 value = 130
            0x01, 0x01, 0x0E, 0x00,  // resolution: 1 value = 14
        ];

        Assert.True(PolarPmdControlPointParser.TryParseSettings(
            data, out var measType, out var settings));

        Assert.Equal(0x00, measType);
        Assert.Single(settings.SampleRates);
        Assert.Equal(130, settings.SampleRates[0]);
        Assert.Single(settings.Resolutions);
        Assert.Equal(14, settings.Resolutions[0]);
    }

    [Fact]
    public void TryParse_TooShort_ReturnsFalse()
    {
        Assert.False(PolarPmdControlPointParser.TryParse(new byte[2], out _));
    }
}
