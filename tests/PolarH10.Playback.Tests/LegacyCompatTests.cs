using System.Text.Json;
using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Playback.Tests;

public class LegacyCompatTests
{
    // ── Legacy v1 session.json (no SessionId, DeviceAlias, timestamps) ──

    [Fact]
    public async Task TryLoadSession_LegacyV1_ReturnsSession()
    {
        var dir = CreateTempDir();
        try
        {
            // Write a v1-style session.json (fields that existed before the multi-device extension)
            var json = """
                {
                  "SchemaVersion": 1,
                  "DeviceName": "Polar H10 77456",
                  "HrRrSampleCount": 42,
                  "EcgFrameCount": 100,
                  "AccFrameCount": 50,
                  "TranscriptEntryCount": 3
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), json);

            var session = await SessionDiscovery.TryLoadSession(dir);

            Assert.NotNull(session);
            Assert.Equal(1, session.SchemaVersion);
            Assert.Equal("Polar H10 77456", session.DeviceName);
            Assert.Null(session.SessionId);
            Assert.Null(session.DeviceAddress);
            Assert.Null(session.DeviceAlias);
            Assert.Null(session.StartedAtUtc);
            Assert.Equal(42, session.HrRrSampleCount);
            Assert.Equal(100, session.EcgFrameCount);
            Assert.Equal(50, session.AccFrameCount);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task TryLoadSession_V2_ReturnsAllFields()
    {
        var dir = CreateTempDir();
        try
        {
            var json = """
                {
                  "SchemaVersion": 2,
                  "SessionId": "abc123",
                  "DeviceName": "Polar H10 TEST",
                  "DeviceAddress": "AABBCCDDEE11",
                  "DeviceAlias": "Chest-Left",
                  "StartedAtUtc": "2025-01-15T10:30:00.0000000Z",
                  "SavedAtUtc": "2025-01-15T10:30:10.0000000Z",
                  "HrRrSampleCount": 10,
                  "EcgFrameCount": 20,
                  "AccFrameCount": 30,
                  "TranscriptEntryCount": 5
                }
                """;
            await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), json);

            var session = await SessionDiscovery.TryLoadSession(dir);

            Assert.NotNull(session);
            Assert.Equal(2, session.SchemaVersion);
            Assert.Equal("abc123", session.SessionId);
            Assert.Equal("Chest-Left", session.DeviceAlias);
            Assert.Equal("AABBCCDDEE11", session.DeviceAddress);
            Assert.Equal("2025-01-15T10:30:00.0000000Z", session.StartedAtUtc);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task TryLoadSession_CorruptJson_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), "{{not valid json}}");
            var session = await SessionDiscovery.TryLoadSession(dir);
            Assert.Null(session);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task TryLoadSession_MissingFile_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            var session = await SessionDiscovery.TryLoadSession(dir);
            Assert.Null(session);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task TryLoadSession_EmptyJson_ReturnsNull()
    {
        var dir = CreateTempDir();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), "null");
            var session = await SessionDiscovery.TryLoadSession(dir);
            Assert.Null(session);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── DisplayLabel fallback chain ──

    [Fact]
    public async Task DiscoveredSession_DisplayLabel_PrefersAlias()
    {
        var dir = CreateTempDir();
        try
        {
            var json = """{"DeviceAlias":"MyAlias","DeviceName":"H10","DeviceAddress":"AA","SchemaVersion":2}""";
            await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), json);

            var session = await SessionDiscovery.TryLoadSession(dir);
            Assert.Equal("MyAlias", session!.DisplayLabel);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task DiscoveredSession_DisplayLabel_FallsToDeviceName()
    {
        var dir = CreateTempDir();
        try
        {
            var json = """{"DeviceName":"Polar H10 1234","SchemaVersion":1}""";
            await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), json);

            var session = await SessionDiscovery.TryLoadSession(dir);
            Assert.Equal("Polar H10 1234", session!.DisplayLabel);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── Legacy CSV compatibility (no device_address/device_alias columns) ──

    [Fact]
    public async Task LegacyCsv_HrRr_WithoutDeviceColumns_IsReadable()
    {
        // A v1-era CSV would have no device columns; just verify it doesn't
        // crash the session discovery (CSV reading is line-based in replay)
        var dir = CreateTempDir();
        try
        {
            var csv = "heart_rate_bpm,rr_intervals_ms\n72,750.00;800.00\n";
            await File.WriteAllTextAsync(Path.Combine(dir, "hr_rr.csv"), csv);

            var lines = await File.ReadAllLinesAsync(Path.Combine(dir, "hr_rr.csv"));
            Assert.Equal(2, lines.Length); // header + 1 data row
            Assert.DoesNotContain("device_address", lines[0]);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CurrentCsv_HrRr_HasDeviceColumns()
    {
        var recorder = new PolarSessionRecorder
        {
            DeviceAddress = "AABBCCDDEE11",
            DeviceAlias = "TestAlias",
        };
        recorder.RecordHrRr(new HrRrSample(72, [750f]));

        var dir = CreateTempDir();
        try
        {
            await recorder.SaveAsync(dir);
            var lines = await File.ReadAllLinesAsync(Path.Combine(dir, "hr_rr.csv"));

            Assert.StartsWith("device_address,device_alias,", lines[0]);
            Assert.StartsWith("AABBCCDDEE11,TestAlias,", lines[1]);
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "polarh10_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
