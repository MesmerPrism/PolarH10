using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Playback.Tests;

public class PolarSessionRecorderTests
{
    [Fact]
    public async Task SaveAsync_EmptySession_CreatesFiles()
    {
        var recorder = new PolarSessionRecorder
        {
            DeviceName = "Test H10",
            DeviceAddress = "001122334455",
        };

        var tempDir = Path.Combine(Path.GetTempPath(), "polarh10_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            await recorder.SaveAsync(tempDir);

            Assert.True(File.Exists(Path.Combine(tempDir, "session.json")));
            Assert.True(File.Exists(Path.Combine(tempDir, "hr_rr.csv")));
            Assert.True(File.Exists(Path.Combine(tempDir, "ecg.csv")));
            Assert.True(File.Exists(Path.Combine(tempDir, "acc.csv")));
            Assert.True(File.Exists(Path.Combine(tempDir, "protocol.jsonl")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task SaveAsync_WithSamples_CsvContainsData()
    {
        var recorder = new PolarSessionRecorder();
        recorder.RecordHrRr(new HrRrSample(72, [750f, 800f]));
        recorder.RecordEcg(new PolarEcgFrame(1000, DateTime.UtcNow.Ticks, [100, -50, 0]));
        recorder.RecordAcc(new PolarAccFrame(2000, DateTime.UtcNow.Ticks, [new AccSampleMg(100, -200, 1000)]));

        var tempDir = Path.Combine(Path.GetTempPath(), "polarh10_test_" + Guid.NewGuid().ToString("N"));

        try
        {
            await recorder.SaveAsync(tempDir);

            var hrLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "hr_rr.csv"));
            Assert.True(hrLines.Length > 1); // header + data

            var ecgLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "ecg.csv"));
            Assert.True(ecgLines.Length > 1);

            var accLines = await File.ReadAllLinesAsync(Path.Combine(tempDir, "acc.csv"));
            Assert.True(accLines.Length > 1);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
}
