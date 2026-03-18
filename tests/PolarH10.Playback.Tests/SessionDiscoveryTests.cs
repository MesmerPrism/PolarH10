using System.Text.Json;
using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Playback.Tests;

public class SessionDiscoveryTests
{
    // ── CaptureRunManifest round-trip ───────────────────────────

    [Fact]
    public async Task CaptureRunManifest_SaveAndLoad_RoundTrips()
    {
        var dir = CreateTempDir();
        try
        {
            var manifest = new CaptureRunManifest
            {
                StartedAtUtc = "2025-06-01T12:00:00.0000000Z",
                StoppedAtUtc = "2025-06-01T12:05:00.0000000Z",
            };
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                DeviceAddress = "AABBCCDDEE11",
                DeviceAlias = "Chest-Left",
                SubFolder = "20250601-120000Z_Chest-Left",
                SessionId = "s1",
                HrRrSampleCount = 100,
                EcgFrameCount = 200,
                AccFrameCount = 300,
            });
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                DeviceAddress = "FF00112233AA",
                DeviceAlias = "Chest-Right",
                SubFolder = "20250601-120000Z_Chest-Right",
                SessionId = "s2",
                HrRrSampleCount = 50,
            });

            var path = Path.Combine(dir, "run.json");
            await CaptureRunManifest.SaveAsync(path, manifest);

            var loaded = await CaptureRunManifest.LoadAsync(path);

            Assert.NotNull(loaded);
            Assert.Equal(1, loaded.SchemaVersion);
            Assert.Equal(manifest.RunId, loaded.RunId);
            Assert.Equal("2025-06-01T12:00:00.0000000Z", loaded.StartedAtUtc);
            Assert.Equal("2025-06-01T12:05:00.0000000Z", loaded.StoppedAtUtc);
            Assert.Equal(2, loaded.DeviceSessions.Count);

            Assert.Equal("Chest-Left", loaded.DeviceSessions[0].DeviceAlias);
            Assert.Equal("20250601-120000Z_Chest-Left", loaded.DeviceSessions[0].SubFolder);
            Assert.Equal(100, loaded.DeviceSessions[0].HrRrSampleCount);

            Assert.Equal("FF00112233AA", loaded.DeviceSessions[1].DeviceAddress);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CaptureRunManifest_LoadAsync_MissingFile_ReturnsNull()
    {
        var result = await CaptureRunManifest.LoadAsync(Path.Combine(Path.GetTempPath(), "nonexistent_run.json"));
        Assert.Null(result);
    }

    // ── Session discovery: standalone sessions ──────────────────

    [Fact]
    public async Task ScanAsync_FindsStandaloneSession()
    {
        var root = CreateTempDir();
        try
        {
            var sessionDir = Path.Combine(root, "my-session");
            Directory.CreateDirectory(sessionDir);
            await WriteMinimalSession(sessionDir, "AABB", "TestDevice");

            var result = await SessionDiscovery.ScanAsync(root);

            Assert.Single(result.StandaloneSessions);
            Assert.Empty(result.CaptureRuns);
            Assert.Equal("AABB", result.StandaloneSessions[0].DeviceAddress);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ScanAsync_FindsMultipleStandaloneSessions()
    {
        var root = CreateTempDir();
        try
        {
            var s1 = Path.Combine(root, "session1");
            var s2 = Path.Combine(root, "session2");
            Directory.CreateDirectory(s1);
            Directory.CreateDirectory(s2);
            await WriteMinimalSession(s1, "ADDR1", "Dev1");
            await WriteMinimalSession(s2, "ADDR2", "Dev2");

            var result = await SessionDiscovery.ScanAsync(root);

            Assert.Equal(2, result.StandaloneSessions.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ScanAsync_NonExistentFolder_ReturnsEmpty()
    {
        var result = await SessionDiscovery.ScanAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Empty(result.StandaloneSessions);
        Assert.Empty(result.CaptureRuns);
    }

    // ── Session discovery: capture runs ──────────────────────────

    [Fact]
    public async Task ScanAsync_FindsCaptureRun_WithChildSessions()
    {
        var root = CreateTempDir();
        try
        {
            var runDir = Path.Combine(root, "capture-run-1");
            Directory.CreateDirectory(runDir);

            // Create two child session folders
            var child1 = Path.Combine(runDir, "dev1-session");
            var child2 = Path.Combine(runDir, "dev2-session");
            Directory.CreateDirectory(child1);
            Directory.CreateDirectory(child2);
            await WriteMinimalSession(child1, "ADDR1", "Dev1", alias: "Left");
            await WriteMinimalSession(child2, "ADDR2", "Dev2", alias: "Right");

            // Write run.json manifest
            var manifest = new CaptureRunManifest
            {
                StartedAtUtc = "2025-01-01T00:00:00Z",
            };
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                DeviceAddress = "ADDR1",
                SubFolder = "dev1-session",
            });
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                DeviceAddress = "ADDR2",
                SubFolder = "dev2-session",
            });
            await CaptureRunManifest.SaveAsync(Path.Combine(runDir, "run.json"), manifest);

            var result = await SessionDiscovery.ScanAsync(root);

            Assert.Single(result.CaptureRuns);
            Assert.Empty(result.StandaloneSessions); // child sessions should NOT appear as standalone

            var run = result.CaptureRuns[0];
            Assert.Equal(2, run.DeviceSessions.Count);
            Assert.Equal("Left", run.DeviceSessions[0].DeviceAlias);
            Assert.Equal("Right", run.DeviceSessions[1].DeviceAlias);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ScanAsync_MixedRunsAndStandalone()
    {
        var root = CreateTempDir();
        try
        {
            // Standalone session
            var standalone = Path.Combine(root, "solo");
            Directory.CreateDirectory(standalone);
            await WriteMinimalSession(standalone, "SOLO_ADDR", "Solo");

            // Capture run
            var runDir = Path.Combine(root, "run");
            Directory.CreateDirectory(runDir);
            var child = Path.Combine(runDir, "child");
            Directory.CreateDirectory(child);
            await WriteMinimalSession(child, "RUN_ADDR", "RunDev");

            var manifest = new CaptureRunManifest();
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                SubFolder = "child",
            });
            await CaptureRunManifest.SaveAsync(Path.Combine(runDir, "run.json"), manifest);

            var result = await SessionDiscovery.ScanAsync(root);

            Assert.Single(result.StandaloneSessions);
            Assert.Single(result.CaptureRuns);
            Assert.Equal("SOLO_ADDR", result.StandaloneSessions[0].DeviceAddress);
            Assert.Single(result.CaptureRuns[0].DeviceSessions);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ScanAsync_RunWithMissingChildFolder_SkipsGracefully()
    {
        var root = CreateTempDir();
        try
        {
            var runDir = Path.Combine(root, "run");
            Directory.CreateDirectory(runDir);

            var manifest = new CaptureRunManifest();
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                SubFolder = "does-not-exist",
            });
            await CaptureRunManifest.SaveAsync(Path.Combine(runDir, "run.json"), manifest);

            var result = await SessionDiscovery.ScanAsync(root);

            Assert.Single(result.CaptureRuns);
            Assert.Empty(result.CaptureRuns[0].DeviceSessions); // missing child not added
        }
        finally { Directory.Delete(root, true); }
    }

    // ── Multi-device recorder integration ───────────────────────

    [Fact]
    public async Task TwoDeviceRecording_ProducesTwoSessions_DiscoverableAsStandalone()
    {
        var root = CreateTempDir();
        try
        {
            // Simulate two independent per-device recordings (no run.json)
            var rec1 = new PolarSessionRecorder
            {
                DeviceAddress = "AABBCCDDEE11",
                DeviceAlias = "Left",
            };
            rec1.RecordHrRr(new HrRrSample(72, [800f]));

            var rec2 = new PolarSessionRecorder
            {
                DeviceAddress = "FF00112233AA",
                DeviceAlias = "Right",
            };
            rec2.RecordHrRr(new HrRrSample(65, [900f, 910f]));

            var folder1 = Path.Combine(root, rec1.GenerateFolderName());
            var folder2 = Path.Combine(root, rec2.GenerateFolderName());
            await rec1.SaveAsync(folder1);
            await rec2.SaveAsync(folder2);

            var result = await SessionDiscovery.ScanAsync(root);

            Assert.Equal(2, result.StandaloneSessions.Count);
            var addresses = result.StandaloneSessions.Select(s => s.DeviceAddress).OrderBy(a => a).ToList();
            Assert.Equal("AABBCCDDEE11", addresses[0]);
            Assert.Equal("FF00112233AA", addresses[1]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task TwoDeviceRecording_WithRunManifest_DiscoverableAsCaptureRun()
    {
        var root = CreateTempDir();
        try
        {
            var rec1 = new PolarSessionRecorder
            {
                DeviceAddress = "AABBCCDDEE11",
                DeviceAlias = "Left",
            };
            rec1.RecordHrRr(new HrRrSample(72, [800f]));
            rec1.RecordEcg(new PolarEcgFrame(1000, DateTime.UtcNow.Ticks, [100, -50]));

            var rec2 = new PolarSessionRecorder
            {
                DeviceAddress = "FF00112233AA",
                DeviceAlias = "Right",
            };
            rec2.RecordHrRr(new HrRrSample(65, [900f]));

            var folder1Name = rec1.GenerateFolderName();
            var folder2Name = rec2.GenerateFolderName();
            await rec1.SaveAsync(Path.Combine(root, folder1Name));
            await rec2.SaveAsync(Path.Combine(root, folder2Name));

            // Write a run manifest (as the GUI would)
            var manifest = new CaptureRunManifest
            {
                StartedAtUtc = DateTimeOffset.UtcNow.UtcDateTime.ToString("O"),
            };
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                DeviceAddress = "AABBCCDDEE11",
                DeviceAlias = "Left",
                SubFolder = folder1Name,
                SessionId = rec1.SessionId,
                HrRrSampleCount = rec1.HrRrCount,
                EcgFrameCount = rec1.EcgFrameCount,
                AccFrameCount = rec1.AccFrameCount,
            });
            manifest.DeviceSessions.Add(new CaptureRunManifest.DeviceSessionEntry
            {
                DeviceAddress = "FF00112233AA",
                DeviceAlias = "Right",
                SubFolder = folder2Name,
                SessionId = rec2.SessionId,
                HrRrSampleCount = rec2.HrRrCount,
            });
            await CaptureRunManifest.SaveAsync(Path.Combine(root, "run.json"), manifest);

            var result = await SessionDiscovery.ScanAsync(root);

            // Should be discovered as a capture run, not standalone
            Assert.Single(result.CaptureRuns);
            Assert.Empty(result.StandaloneSessions);

            var run = result.CaptureRuns[0];
            Assert.Equal(2, run.DeviceSessions.Count);

            // Verify each child session has the right identity
            var left = run.DeviceSessions.First(s => s.DeviceAddress == "AABBCCDDEE11");
            Assert.Equal("Left", left.DeviceAlias);
            Assert.Equal(1, left.HrRrSampleCount);
            Assert.Equal(1, left.EcgFrameCount);

            var right = run.DeviceSessions.First(s => s.DeviceAddress == "FF00112233AA");
            Assert.Equal("Right", right.DeviceAlias);
            Assert.Equal(1, right.HrRrSampleCount);
        }
        finally { Directory.Delete(root, true); }
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "polarh10_disc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WriteMinimalSession(string dir, string address, string name, string? alias = null)
    {
        var json = JsonSerializer.Serialize(new
        {
            SchemaVersion = 2,
            DeviceAddress = address,
            DeviceName = name,
            DeviceAlias = alias,
            HrRrSampleCount = 0,
            EcgFrameCount = 0,
            AccFrameCount = 0,
            TranscriptEntryCount = 0,
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(dir, "session.json"), json);
    }
}
