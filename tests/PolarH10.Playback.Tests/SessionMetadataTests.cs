using System.Text.Json;
using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Playback.Tests;

public class SessionMetadataTests
{
    [Fact]
    public async Task SaveAsync_SchemaVersion2_SessionIdAndAlias()
    {
        var recorder = new PolarSessionRecorder
        {
            DeviceName = "Polar H10 TEST",
            DeviceAddress = "AABBCCDDEE11",
            DeviceAlias = "Chest-Left",
        };

        var tempDir = Path.Combine(Path.GetTempPath(), "polarh10_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            await recorder.SaveAsync(tempDir);

            var json = await File.ReadAllTextAsync(Path.Combine(tempDir, "session.json"));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.Equal(2, root.GetProperty("SchemaVersion").GetInt32());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("SessionId").GetString()));
            Assert.Equal("AABBCCDDEE11", root.GetProperty("DeviceAddress").GetString());
            Assert.Equal("Chest-Left", root.GetProperty("DeviceAlias").GetString());
            Assert.Equal("Polar H10 TEST", root.GetProperty("DeviceName").GetString());
            Assert.False(string.IsNullOrEmpty(root.GetProperty("StartedAtUtc").GetString()));
            Assert.False(string.IsNullOrEmpty(root.GetProperty("SavedAtUtc").GetString()));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SessionId_IsUnique()
    {
        var r1 = new PolarSessionRecorder();
        var r2 = new PolarSessionRecorder();

        Assert.NotEqual(r1.SessionId, r2.SessionId);
    }

    [Fact]
    public void GenerateFolderName_UsesAlias()
    {
        var rec = new PolarSessionRecorder
        {
            DeviceAddress = "AABBCCDDEE11",
            DeviceAlias = "Chest Left",
        };

        var name = rec.GenerateFolderName();

        // Should end with sanitized alias (space → '-')
        Assert.EndsWith("_Chest-Left", name);
        Assert.Matches(@"^\d{8}-\d{6}Z_Chest-Left$", name);
    }

    [Fact]
    public void GenerateFolderName_FallsBackToAddress()
    {
        var rec = new PolarSessionRecorder
        {
            DeviceAddress = "AABBCCDDEE11",
        };

        var name = rec.GenerateFolderName();

        Assert.EndsWith("_AABBCCDDEE11", name);
    }

    [Fact]
    public async Task CsvFiles_ContainDeviceIdentityColumns()
    {
        var recorder = new PolarSessionRecorder
        {
            DeviceAddress = "AABBCCDDEE11",
            DeviceAlias = "TestDevice",
        };
        recorder.RecordHrRr(new HrRrSample(72, [750f]));
        recorder.RecordEcg(new PolarEcgFrame(1000, DateTime.UtcNow.Ticks, [100]));
        recorder.RecordAcc(new PolarAccFrame(2000, DateTime.UtcNow.Ticks, [new AccSampleMg(10, -20, 100)]));

        var tempDir = Path.Combine(Path.GetTempPath(), "polarh10_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            await recorder.SaveAsync(tempDir);

            // HR CSV
            var hrLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "hr_rr.csv"));
            Assert.StartsWith("device_address,device_alias,", hrLines[0]);
            Assert.StartsWith("AABBCCDDEE11,TestDevice,", hrLines[1]);

            // ECG CSV
            var ecgLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "ecg.csv"));
            Assert.StartsWith("device_address,device_alias,", ecgLines[0]);
            Assert.StartsWith("AABBCCDDEE11,TestDevice,", ecgLines[1]);

            // ACC CSV
            var accLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "acc.csv"));
            Assert.StartsWith("device_address,device_alias,", accLines[0]);
            Assert.StartsWith("AABBCCDDEE11,TestDevice,", accLines[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CsvFiles_WorkWithoutAlias()
    {
        var recorder = new PolarSessionRecorder
        {
            DeviceAddress = "AABBCCDDEE11",
        };
        recorder.RecordHrRr(new HrRrSample(72, [750f]));

        var tempDir = Path.Combine(Path.GetTempPath(), "polarh10_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            await recorder.SaveAsync(tempDir);

            var hrLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "hr_rr.csv"));
            // Should have empty alias column
            Assert.StartsWith("AABBCCDDEE11,,", hrLines[1]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
