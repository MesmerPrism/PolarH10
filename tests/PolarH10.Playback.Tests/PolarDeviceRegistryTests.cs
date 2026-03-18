using System.Text.Json;
using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Playback.Tests;

public class PolarDeviceRegistryTests
{
    private string CreateTempPath() =>
        Path.Combine(Path.GetTempPath(), $"polarh10_reg_{Guid.NewGuid():N}.json");

    [Fact]
    public void RecordSeen_CreatesNewIdentity()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());

        var id = reg.RecordSeen("AABBCCDDEE11", "Polar H10 EE11");

        Assert.Equal("AABBCCDDEE11", id.BluetoothAddress);
        Assert.Equal("Polar H10 EE11", id.AdvertisedName);
        Assert.Null(id.UserAlias);
        Assert.Single(reg.Devices);
    }

    [Fact]
    public void RecordSeen_UpdatesAdvertisedName()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());
        reg.RecordSeen("AABBCCDDEE11", "Old Name");

        reg.RecordSeen("AABBCCDDEE11", "New Name");

        Assert.Equal("New Name", reg.Get("AABBCCDDEE11")!.AdvertisedName);
        Assert.Single(reg.Devices);
    }

    [Fact]
    public void RecordConnected_SetsLastConnectedAt()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());
        var before = DateTimeOffset.UtcNow;

        var id = reg.RecordConnected("AABBCCDDEE11");

        Assert.True(id.LastConnectedAtUtc >= before);
    }

    [Fact]
    public void SetAlias_StoresAndRetrieves()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());
        reg.RecordSeen("AABBCCDDEE11");

        Assert.True(reg.SetAlias("AABBCCDDEE11", "My Chest Strap"));
        Assert.Equal("My Chest Strap", reg.Get("AABBCCDDEE11")!.UserAlias);
        Assert.Equal("My Chest Strap", reg.Get("AABBCCDDEE11")!.DisplayName);
    }

    [Fact]
    public void SetAlias_ClearsWithNullOrWhitespace()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());
        reg.RecordSeen("AABBCCDDEE11");
        reg.SetAlias("AABBCCDDEE11", "Alias");

        reg.SetAlias("AABBCCDDEE11", "  ");

        Assert.Null(reg.Get("AABBCCDDEE11")!.UserAlias);
    }

    [Fact]
    public void SetAlias_ReturnsFalseForUnknownDevice()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());

        Assert.False(reg.SetAlias("UNKNOWN", "Alias"));
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());
        reg.RecordSeen("aabbccddee11");

        Assert.NotNull(reg.Get("AABBCCDDEE11"));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var path = CreateTempPath();
        try
        {
            var reg = new PolarDeviceRegistry(path);
            reg.RecordSeen("AABBCCDDEE11", "Polar H10 EE11");
            reg.SetAlias("AABBCCDDEE11", "Chest-Left");
            reg.RecordConnected("AABBCCDDEE11");
            reg.RecordSeen("FF00112233AA", "Polar H10 33AA");
            reg.Save();

            var loaded = new PolarDeviceRegistry(path);
            loaded.Load();

            Assert.Equal(2, loaded.Devices.Count);
            var d1 = loaded.Get("AABBCCDDEE11");
            Assert.NotNull(d1);
            Assert.Equal("Chest-Left", d1.UserAlias);
            Assert.Equal("Polar H10 EE11", d1.AdvertisedName);
            Assert.NotEqual(default, d1.LastConnectedAtUtc);

            var d2 = loaded.Get("FF00112233AA");
            Assert.NotNull(d2);
            Assert.Null(d2.UserAlias);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SaveAsyncAndLoadAsync_RoundTrips()
    {
        var path = CreateTempPath();
        try
        {
            var reg = new PolarDeviceRegistry(path);
            reg.RecordSeen("AABBCCDDEE11", "Polar H10 EE11");
            reg.SetAlias("AABBCCDDEE11", "Test");
            await reg.SaveAsync();

            var loaded = new PolarDeviceRegistry(path);
            await loaded.LoadAsync();

            Assert.Single(loaded.Devices);
            Assert.Equal("Test", loaded.Get("AABBCCDDEE11")!.UserAlias);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        var path = CreateTempPath();
        File.WriteAllText(path, "not valid json {{{{");
        try
        {
            var reg = new PolarDeviceRegistry(path);
            reg.Load();

            Assert.Empty(reg.Devices);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_StartsEmpty()
    {
        var reg = new PolarDeviceRegistry(CreateTempPath());
        reg.Load();

        Assert.Empty(reg.Devices);
    }

    [Fact]
    public void DisplayName_ReturnsAliasOverAddress()
    {
        var id = new PolarDeviceIdentity
        {
            BluetoothAddress = "AABBCCDDEE11",
            UserAlias = "My Strap",
        };

        Assert.Equal("My Strap", id.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToAddress()
    {
        var id = new PolarDeviceIdentity
        {
            BluetoothAddress = "AABBCCDDEE11",
        };

        Assert.Equal("AABBCCDDEE11", id.DisplayName);
    }
}
