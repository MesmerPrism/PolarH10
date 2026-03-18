using PolarH10.Transport.Windows;
using Xunit;

namespace PolarH10.Transport.Windows.Tests;

/// <summary>
/// Basic smoke tests for the Windows BLE adapter factory.
/// Full integration tests require a live Polar H10 device.
/// </summary>
public class WindowsBleAdapterFactoryTests
{
    [Fact]
    public void CreateScanner_ReturnsInstance()
    {
        var factory = new WindowsBleAdapterFactory();
        using var scanner = (WindowsBleScanner)factory.CreateScanner();
        Assert.NotNull(scanner);
    }

    [Fact]
    public void CreateConnection_ReturnsInstance()
    {
        var factory = new WindowsBleAdapterFactory();
        var connection = factory.CreateConnection("001122334455");
        Assert.NotNull(connection);
        Assert.Equal("001122334455", connection.DeviceAddress);
    }
}
