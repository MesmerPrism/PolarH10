using System.Reflection;
using System.CommandLine;
using PolarH10.Transport.Abstractions;
using PolarH10.Transport.Synthetic;

namespace PolarH10.Cli;

internal static class CliTransportOptions
{
    private const string WindowsTransportAssemblyName = "PolarH10.Transport.Windows";
    private const string WindowsTransportFactoryTypeName = "PolarH10.Transport.Windows.WindowsBleAdapterFactory";

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
    {
        if (string.Equals(transport, "synthetic", StringComparison.OrdinalIgnoreCase))
            return new SyntheticBleAdapterFactory(new SyntheticTransportOptions { PipeBaseName = syntheticPipeBaseName });

        if (string.Equals(transport, "windows", StringComparison.OrdinalIgnoreCase))
            return CreateWindowsFactory();

        throw new ArgumentOutOfRangeException(
            nameof(transport),
            transport,
            "Unsupported transport. Use 'windows' or 'synthetic'.");
    }

    private static IBleAdapterFactory CreateWindowsFactory()
    {
        try
        {
            string assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{WindowsTransportAssemblyName}.dll");
            Assembly assembly = File.Exists(assemblyPath)
                ? Assembly.LoadFrom(assemblyPath)
                : Assembly.Load(WindowsTransportAssemblyName);

            Type factoryType = assembly.GetType(WindowsTransportFactoryTypeName, throwOnError: true)!;
            object? instance = Activator.CreateInstance(factoryType);
            if (instance is IBleAdapterFactory factory)
                return factory;

            throw new InvalidOperationException(
                $"Type '{WindowsTransportFactoryTypeName}' did not implement {nameof(IBleAdapterFactory)}.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                "The Windows transport is unavailable. Ensure PolarH10.Transport.Windows.dll is present beside the CLI output, or use '--transport synthetic' for the synthetic pipe workflow.",
                ex);
        }
    }

    public static Exception RewriteTransportException(
        string transport,
        string syntheticPipeBaseName,
        string operation,
        Exception exception)
    {
        if (string.Equals(transport, "synthetic", StringComparison.OrdinalIgnoreCase) &&
            exception is TimeoutException)
        {
            return new InvalidOperationException(
                $"The synthetic transport did not respond during {operation}. Start SyntheticBio with 'serve --pipe {syntheticPipeBaseName}' first, then retry the PolarH10 command.",
                exception);
        }

        return exception;
    }
}
