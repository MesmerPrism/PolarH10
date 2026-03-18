using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public class PolarPmdCommandBuilderTests
{
    [Fact]
    public void BuildGetSettingsRequest_Ecg_CorrectBytes()
    {
        var cmd = PolarPmdCommandBuilder.BuildGetSettingsRequest(PolarGattIds.MeasurementTypeEcg);
        Assert.Equal(new byte[] { 0x01, 0x00 }, cmd);
    }

    [Fact]
    public void BuildGetSettingsRequest_Acc_CorrectBytes()
    {
        var cmd = PolarPmdCommandBuilder.BuildGetSettingsRequest(PolarGattIds.MeasurementTypeAcc);
        Assert.Equal(new byte[] { 0x01, 0x02 }, cmd);
    }

    [Fact]
    public void BuildStartEcgRequest_Default_StartsWithOpcode02()
    {
        var cmd = PolarPmdCommandBuilder.BuildStartEcgRequest();
        Assert.Equal(0x02, cmd[0]);
        Assert.Equal(PolarGattIds.MeasurementTypeEcg, cmd[1]);
    }

    [Fact]
    public void BuildStartAccRequest_Default_StartsWithOpcode02()
    {
        var cmd = PolarPmdCommandBuilder.BuildStartAccRequest();
        Assert.Equal(0x02, cmd[0]);
        Assert.Equal(PolarGattIds.MeasurementTypeAcc, cmd[1]);
    }

    [Fact]
    public void BuildStopRequest_Ecg_CorrectBytes()
    {
        var cmd = PolarPmdCommandBuilder.BuildStopRequest(PolarGattIds.MeasurementTypeEcg);
        Assert.Equal(new byte[] { 0x03, 0x00 }, cmd);
    }
}
