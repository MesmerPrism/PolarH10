using PolarH10.Transport.Abstractions;
using PolarH10.Transport.Synthetic;
using PolarH10.Transport.Windows;

namespace PolarH10.App;

internal sealed record AppTransportSettings(string TransportName, string SyntheticPipeBaseName)
{
    public static AppTransportSettings FromEnvironmentAndArgs()
    {
        string transportName = Environment.GetEnvironmentVariable("POLARH10_TRANSPORT") ?? "windows";
        string syntheticPipeBaseName = Environment.GetEnvironmentVariable("POLARH10_SYNTHETIC_PIPE") ?? "polarh10-synth";

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--transport", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                transportName = args[i + 1];
            else if (string.Equals(args[i], "--synthetic-pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                syntheticPipeBaseName = args[i + 1];
        }

        return new AppTransportSettings(
            string.IsNullOrWhiteSpace(transportName) ? "windows" : transportName.Trim(),
            string.IsNullOrWhiteSpace(syntheticPipeBaseName) ? "polarh10-synth" : syntheticPipeBaseName.Trim());
    }

    public IBleAdapterFactory CreateFactory()
        => string.Equals(TransportName, "synthetic", StringComparison.OrdinalIgnoreCase)
            ? new SyntheticBleAdapterFactory(new SyntheticTransportOptions { PipeBaseName = SyntheticPipeBaseName })
            : new WindowsBleAdapterFactory();
}
