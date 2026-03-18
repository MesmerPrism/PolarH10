using PolarH10.Protocol;
using Xunit;

namespace PolarH10.Protocol.Tests;

public class PolarMtuRetryPlannerTests
{
    [Fact]
    public void BuildOrderedCandidates_DesiredFirst_NoDuplicates()
    {
        var result = PolarMtuRetryPlanner.BuildOrderedCandidates(232, [247, 232, 185, 128]);

        Assert.Equal(232, result[0]);
        Assert.DoesNotContain(result.Skip(1).ToArray(), x => x == 232);
        Assert.Equal(4, result.Length); // 232, 247, 185, 128
    }

    [Fact]
    public void BuildOrderedCandidates_InvalidMtu_Excluded()
    {
        var result = PolarMtuRetryPlanner.BuildOrderedCandidates(20, [23, 10, 128]);

        Assert.Single(result);
        Assert.Equal(128, result[0]);
    }

    [Fact]
    public void BuildOrderedCandidates_NullCandidates_ReturnsDesiredOnly()
    {
        var result = PolarMtuRetryPlanner.BuildOrderedCandidates(232, null);
        Assert.Single(result);
        Assert.Equal(232, result[0]);
    }
}
