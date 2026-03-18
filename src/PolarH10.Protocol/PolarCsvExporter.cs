using System.Globalization;
using System.Text;

namespace PolarH10.Protocol;

/// <summary>
/// Exports decoded Polar H10 session data to CSV files.
/// </summary>
public static class PolarCsvExporter
{
    /// <summary>
    /// Write HR/RR samples to a CSV file.
    /// </summary>
    public static async Task WriteHrRrCsvAsync(
        string path,
        IReadOnlyList<HrRrSample> samples,
        string? deviceAddress = null,
        string? deviceAlias = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("device_address,device_alias,heart_rate_bpm,rr_intervals_ms");

        var addr = Escape(deviceAddress ?? "");
        var alias = Escape(deviceAlias ?? "");

        foreach (var s in samples)
        {
            string rr = s.RrIntervalsMs.Length > 0
                ? string.Join(';', s.RrIntervalsMs.Select(v => v.ToString("F2", CultureInfo.InvariantCulture)))
                : "";
            sb.Append(addr);
            sb.Append(',');
            sb.Append(alias);
            sb.Append(',');
            sb.Append(s.HeartRateBpm.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(rr);
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }

    /// <summary>
    /// Write ECG frames to a CSV file (one row per sample).
    /// </summary>
    public static async Task WriteEcgCsvAsync(
        string path,
        IReadOnlyList<PolarEcgFrame> frames,
        string? deviceAddress = null,
        string? deviceAlias = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("device_address,device_alias,sensor_timestamp_ns,received_utc_ticks,sample_index,microvolts");

        var addr = Escape(deviceAddress ?? "");
        var alias = Escape(deviceAlias ?? "");

        foreach (var frame in frames)
        {
            for (int i = 0; i < frame.MicroVolts.Length; i++)
            {
                sb.Append(addr);
                sb.Append(',');
                sb.Append(alias);
                sb.Append(',');
                sb.Append(frame.SensorTimestampNs.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(frame.ReceivedUtcTicks.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.AppendLine(frame.MicroVolts[i].ToString(CultureInfo.InvariantCulture));
            }
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }

    /// <summary>
    /// Write ACC frames to a CSV file (one row per sample).
    /// </summary>
    public static async Task WriteAccCsvAsync(
        string path,
        IReadOnlyList<PolarAccFrame> frames,
        string? deviceAddress = null,
        string? deviceAlias = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("device_address,device_alias,sensor_timestamp_ns,received_utc_ticks,sample_index,x_mg,y_mg,z_mg");

        var addr = Escape(deviceAddress ?? "");
        var alias = Escape(deviceAlias ?? "");

        foreach (var frame in frames)
        {
            for (int i = 0; i < frame.Samples.Length; i++)
            {
                var s = frame.Samples[i];
                sb.Append(addr);
                sb.Append(',');
                sb.Append(alias);
                sb.Append(',');
                sb.Append(frame.SensorTimestampNs.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(frame.ReceivedUtcTicks.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(s.X.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(s.Y.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.AppendLine(s.Z.ToString(CultureInfo.InvariantCulture));
            }
        }

        await File.WriteAllTextAsync(path, sb.ToString(), ct);
    }

    /// <summary>
    /// RFC 4180 CSV-escape a value if it contains commas, quotes, or newlines.
    /// </summary>
    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
