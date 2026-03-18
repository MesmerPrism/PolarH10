using PolarH10.Protocol;
using PolarH10.Transport.Windows.Tests.Mocks;
using Xunit;

namespace PolarH10.Transport.Windows.Tests;

public class PolarMultiDeviceCoordinatorTests
{
    private static (MockBleAdapterFactory Factory, PolarDeviceRegistry Registry, string RegistryPath) CreateTestSetup()
    {
        var factory = new MockBleAdapterFactory();
        var regPath = Path.Combine(Path.GetTempPath(), $"polarh10_test_{Guid.NewGuid():N}.json");
        var registry = new PolarDeviceRegistry(regPath);
        return (factory, registry, regPath);
    }

    [Fact]
    public async Task ConnectAsync_SingleDevice_ReturnsContext()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);

            var ctx = await coordinator.ConnectAsync("AABBCCDDEE11", "Polar H10 EE11");

            Assert.NotNull(ctx);
            Assert.Equal("AABBCCDDEE11", ctx.BluetoothAddress);
            Assert.Equal(DeviceConnectionStatus.Connected, ctx.Status);
            Assert.Single(coordinator.Devices);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ConnectAsync_TwoDevices_BothConnected()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);

            var ctx1 = await coordinator.ConnectAsync("AABBCCDDEE11", "Polar H10 #1");
            var ctx2 = await coordinator.ConnectAsync("FF00112233AA", "Polar H10 #2");

            Assert.Equal(2, coordinator.Devices.Count);
            Assert.Equal(DeviceConnectionStatus.Connected, ctx1.Status);
            Assert.Equal(DeviceConnectionStatus.Connected, ctx2.Status);
            Assert.NotSame(ctx1, ctx2);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ConnectAsync_DuplicateAddress_Throws()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.ConnectAsync("AABBCCDDEE11"));
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ConnectAsync_CaseInsensitiveAddress_RejectsDuplicate()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("aabbccddee11");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.ConnectAsync("AABBCCDDEE11"));
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task DisconnectAsync_RemovesDevice()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");
            await coordinator.ConnectAsync("FF00112233AA");

            await coordinator.DisconnectAsync("AABBCCDDEE11");

            Assert.Single(coordinator.Devices);
            Assert.Null(coordinator.GetDevice("AABBCCDDEE11"));
            Assert.NotNull(coordinator.GetDevice("FF00112233AA"));
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task DisconnectAsync_OneDevice_DoesNotAffectOther()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            var ctx1 = await coordinator.ConnectAsync("AABBCCDDEE11");
            var ctx2 = await coordinator.ConnectAsync("FF00112233AA");

            await coordinator.DisconnectAsync("AABBCCDDEE11");

            // ctx2 should still be untouched
            Assert.Equal(DeviceConnectionStatus.Connected, ctx2.Status);
            var remaining = coordinator.GetDevice("FF00112233AA");
            Assert.NotNull(remaining);
            Assert.Equal(DeviceConnectionStatus.Connected, remaining.Status);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task DisconnectAsync_UnknownAddress_NoOp()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);

            // Should not throw
            await coordinator.DisconnectAsync("DOESNOTEXIST");
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ConnectAsync_AfterDisconnect_CanReconnect()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");
            await coordinator.DisconnectAsync("AABBCCDDEE11");

            var ctx = await coordinator.ConnectAsync("AABBCCDDEE11");

            Assert.NotNull(ctx);
            Assert.Equal(DeviceConnectionStatus.Connected, ctx.Status);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ConnectAsync_FailedConnection_SetsErrorStatus()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        var mockConn = factory.RegisterDevice("FAILDEVICE");
        mockConn.ConnectShouldFail = true;

        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.ConnectAsync("FAILDEVICE"));

            var ctx = coordinator.GetDevice("FAILDEVICE");
            Assert.NotNull(ctx);
            Assert.Equal(DeviceConnectionStatus.Error, ctx.Status);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task DeviceAddedAndRemoved_EventsFire()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            var addedList = new List<string>();
            var removedList = new List<string>();

            coordinator.DeviceAdded += ctx => addedList.Add(ctx.BluetoothAddress);
            coordinator.DeviceRemoved += ctx => removedList.Add(ctx.BluetoothAddress);

            await coordinator.ConnectAsync("AABBCCDDEE11");
            await coordinator.ConnectAsync("FF00112233AA");
            await coordinator.DisconnectAsync("AABBCCDDEE11");

            Assert.Equal(2, addedList.Count);
            Assert.Contains("AABBCCDDEE11", addedList);
            Assert.Contains("FF00112233AA", addedList);
            Assert.Single(removedList);
            Assert.Equal("AABBCCDDEE11", removedList[0]);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task StatusChanged_FiresOnConnectAndDisconnect()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            var statusLog = new List<(string Addr, DeviceConnectionStatus Status)>();

            coordinator.DeviceStatusChanged += ctx =>
                statusLog.Add((ctx.BluetoothAddress, ctx.Status));

            await coordinator.ConnectAsync("AABBCCDDEE11");
            await coordinator.DisconnectAsync("AABBCCDDEE11");

            // Expect: Connecting → Connected → Disconnected (from disconnect)
            Assert.Contains(statusLog, x => x is { Addr: "AABBCCDDEE11", Status: DeviceConnectionStatus.Connecting });
            Assert.Contains(statusLog, x => x is { Addr: "AABBCCDDEE11", Status: DeviceConnectionStatus.Connected });
            Assert.Contains(statusLog, x => x is { Addr: "AABBCCDDEE11", Status: DeviceConnectionStatus.Disconnected });
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task RegistryPopulated_AfterConnect()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11", "Polar H10 EE11");
            await coordinator.ConnectAsync("FF00112233AA", "Polar H10 33AA");

            Assert.Equal(2, registry.Devices.Count);
            var d1 = registry.Get("AABBCCDDEE11");
            Assert.NotNull(d1);
            Assert.Equal("Polar H10 EE11", d1.AdvertisedName);
            Assert.NotEqual(default, d1.LastConnectedAtUtc);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ContextIdentity_HasAlias_WhenSet()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            registry.RecordSeen("AABBCCDDEE11", "Polar H10 EE11");
            registry.SetAlias("AABBCCDDEE11", "Chest-Left");

            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            var ctx = await coordinator.ConnectAsync("AABBCCDDEE11");

            Assert.Equal("Chest-Left", ctx.Identity.UserAlias);
            Assert.Equal("Chest-Left", ctx.Identity.DisplayName);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task StartRecording_CreatesRecorder()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            registry.RecordSeen("AABBCCDDEE11", "Polar H10 EE11");
            registry.SetAlias("AABBCCDDEE11", "Test Strap");
            var ctx = await coordinator.ConnectAsync("AABBCCDDEE11");

            var recorder = coordinator.StartRecording("AABBCCDDEE11");

            Assert.NotNull(recorder);
            Assert.Equal("AABBCCDDEE11", recorder.DeviceAddress);
            Assert.Equal("Test Strap", recorder.DeviceAlias);
            Assert.Equal("Polar H10 EE11", recorder.DeviceName);
            Assert.NotNull(ctx.Recorder);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task StartRecording_Duplicate_Throws()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");
            coordinator.StartRecording("AABBCCDDEE11");

            Assert.Throws<InvalidOperationException>(() =>
                coordinator.StartRecording("AABBCCDDEE11"));
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task StopRecordingAsync_SavesAndClearsRecorder()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        var outputDir = Path.Combine(Path.GetTempPath(), $"polarh10_rec_{Guid.NewGuid():N}");
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");
            coordinator.StartRecording("AABBCCDDEE11");

            var recorder = await coordinator.StopRecordingAsync("AABBCCDDEE11", outputDir);

            Assert.NotNull(recorder);
            Assert.Null(coordinator.GetDevice("AABBCCDDEE11")?.Recorder);
            Assert.True(File.Exists(Path.Combine(outputDir, "session.json")));
        }
        finally
        {
            TryDelete(regPath);
            if (Directory.Exists(outputDir)) Directory.Delete(outputDir, true);
        }
    }

    [Fact]
    public async Task StopRecordingAsync_NoActiveRecording_Throws()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => coordinator.StopRecordingAsync("AABBCCDDEE11", "."));
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task ParallelRecording_TwoDevices_Independent()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        var outDir1 = Path.Combine(Path.GetTempPath(), $"polarh10_rec_{Guid.NewGuid():N}");
        var outDir2 = Path.Combine(Path.GetTempPath(), $"polarh10_rec_{Guid.NewGuid():N}");
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11", "Polar H10 #1");
            await coordinator.ConnectAsync("FF00112233AA", "Polar H10 #2");

            var rec1 = coordinator.StartRecording("AABBCCDDEE11");
            var rec2 = coordinator.StartRecording("FF00112233AA");

            // Simulate data arriving for device 1 only
            rec1.RecordHrRr(new HrRrSample(72, [800f]));
            rec1.RecordHrRr(new HrRrSample(73, [810f]));

            // Simulate data arriving for device 2 only
            rec2.RecordHrRr(new HrRrSample(65, [920f]));

            await coordinator.StopRecordingAsync("AABBCCDDEE11", outDir1);
            await coordinator.StopRecordingAsync("FF00112233AA", outDir2);

            // Verify each session has only its own data
            var json1 = await File.ReadAllTextAsync(Path.Combine(outDir1, "session.json"));
            Assert.Contains("\"AABBCCDDEE11\"", json1);
            Assert.Contains("\"HrRrSampleCount\": 2", json1);

            var json2 = await File.ReadAllTextAsync(Path.Combine(outDir2, "session.json"));
            Assert.Contains("\"FF00112233AA\"", json2);
            Assert.Contains("\"HrRrSampleCount\": 1", json2);

            var hrCsv1 = await File.ReadAllLinesAsync(Path.Combine(outDir1, "hr_rr.csv"));
            Assert.Equal(3, hrCsv1.Length); // header + 2 rows
            Assert.StartsWith("AABBCCDDEE11,", hrCsv1[1]);

            var hrCsv2 = await File.ReadAllLinesAsync(Path.Combine(outDir2, "hr_rr.csv"));
            Assert.Equal(2, hrCsv2.Length); // header + 1 row
            Assert.StartsWith("FF00112233AA,", hrCsv2[1]);
        }
        finally
        {
            TryDelete(regPath);
            if (Directory.Exists(outDir1)) Directory.Delete(outDir1, true);
            if (Directory.Exists(outDir2)) Directory.Delete(outDir2, true);
        }
    }

    [Fact]
    public async Task DisposeAsync_DisconnectsAllDevices()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            var removedList = new List<string>();
            var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            coordinator.DeviceRemoved += ctx => removedList.Add(ctx.BluetoothAddress);

            await coordinator.ConnectAsync("AABBCCDDEE11");
            await coordinator.ConnectAsync("FF00112233AA");

            await coordinator.DisposeAsync();

            Assert.Empty(coordinator.Devices);
            Assert.Equal(2, removedList.Count);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task SimulatedDisconnect_OnlyAffectsOwningDevice()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        var conn1 = factory.RegisterDevice("AABBCCDDEE11");
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            var ctx1 = await coordinator.ConnectAsync("AABBCCDDEE11");
            var ctx2 = await coordinator.ConnectAsync("FF00112233AA");

            // Simulate device 1 dropping
            conn1.SimulateDisconnect();
            // Give events time to propagate
            await Task.Delay(50);

            Assert.Equal(DeviceConnectionStatus.Disconnected, ctx1.Status);
            Assert.Equal(DeviceConnectionStatus.Connected, ctx2.Status);
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task GetDevice_ReturnsNull_ForUnknown()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);

            Assert.Null(coordinator.GetDevice("DOESNOTEXIST"));
        }
        finally { TryDelete(regPath); }
    }

    [Fact]
    public async Task Devices_ReturnsSnapshotNotLiveReference()
    {
        var (factory, registry, regPath) = CreateTestSetup();
        try
        {
            await using var coordinator = new PolarMultiDeviceCoordinator(factory, registry);
            await coordinator.ConnectAsync("AABBCCDDEE11");

            var snapshot = coordinator.Devices;

            await coordinator.ConnectAsync("FF00112233AA");

            // Snapshot should still have 1 device
            Assert.Single(snapshot);
            Assert.Equal(2, coordinator.Devices.Count);
        }
        finally { TryDelete(regPath); }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
