using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolarH10.Transport.Synthetic;

internal static class SyntheticPipeProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string GetDiscoveryPipeName(SyntheticTransportOptions options)
        => $"{options.Normalize().PipeBaseName}.discovery";

    public static string GetDevicePipeName(SyntheticTransportOptions options, string deviceAddress)
        => $"{options.Normalize().PipeBaseName}.device.{SanitizeDeviceTag(deviceAddress)}";

    public static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(message, JsonOptions);
        byte[] payload = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<T?> ReadMessageAsync<T>(Stream stream, CancellationToken ct = default)
    {
        string? line = await ReadLineAsync(stream, ct);
        if (string.IsNullOrWhiteSpace(line))
            return default;

        return JsonSerializer.Deserialize<T>(line, JsonOptions);
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[1];
        bool sawAnyData = false;

        while (true)
        {
            int bytesRead = await stream.ReadAsync(chunk, ct);
            if (bytesRead == 0)
                break;

            sawAnyData = true;
            if (chunk[0] == (byte)'\n')
                break;

            buffer.WriteByte(chunk[0]);
        }

        if (!sawAnyData && buffer.Length == 0)
            return null;

        return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
    }

    private static string SanitizeDeviceTag(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char c in value)
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '_');

        return builder.ToString();
    }

    internal sealed class ClientEnvelope
    {
        public string? Type { get; set; }
        public int? DurationMs { get; set; }
        public string? DeviceAddress { get; set; }
    }

    internal sealed class ServerEnvelope
    {
        public string? Type { get; set; }
        public List<DiscoveredDevice>? Devices { get; set; }
        public byte[]? Payload { get; set; }
        public BreathingTelemetryEnvelope? Breathing { get; set; }
        public bool? Connected { get; set; }
        public string? Reason { get; set; }
    }

    internal sealed class DiscoveredDevice
    {
        public string Address { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Rssi { get; set; }
    }

    internal sealed class BreathingTelemetryEnvelope
    {
        public DateTimeOffset SampleTimeUtc { get; set; }
        public float Volume01 { get; set; }
        public string State { get; set; } = "Pausing";
        public bool HasTracking { get; set; } = true;
        public bool IsStale { get; set; }
        public string? Tag { get; set; }
    }
}
