using System.CommandLine;
using PolarH10.Transport.Abstractions;
using PolarH10.Transport.Synthetic;
using PolarH10.Transport.Windows;

namespace PolarH10.Cli;

internal static class CliTransportOptions
{
    public static Option<string> CreateTransportOption()
        => new(
            "--transport",
            () => "windows",
            "Transport backend: windows or synthetic");

    public static Option<string> CreateSyntheticPipeOption()
        => new(
            "--synthetic-pipe",
            () => "polarh10-synth",
            "Named-pipe base name used by the synthetic transport");

    public static IBleAdapterFactory CreateFactory(string transport, string syntheticPipeBaseName)
        => string.Equals(transport, "synthetic", StringComparison.OrdinalIgnoreCase)
            ? new SyntheticBleAdapterFactory(new SyntheticTransportOptions { PipeBaseName = syntheticPipeBaseName })
            : new WindowsBleAdapterFactory();
}
