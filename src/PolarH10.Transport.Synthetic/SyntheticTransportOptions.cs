namespace PolarH10.Transport.Synthetic;

public sealed record SyntheticTransportOptions
{
    public string PipeBaseName { get; init; } = "polarh10-synth";

    public static SyntheticTransportOptions CreateDefault() => new();

    public SyntheticTransportOptions Normalize()
    {
        string pipeBaseName = string.IsNullOrWhiteSpace(PipeBaseName)
            ? "polarh10-synth"
            : PipeBaseName.Trim();

        return this with { PipeBaseName = pipeBaseName };
    }
}
