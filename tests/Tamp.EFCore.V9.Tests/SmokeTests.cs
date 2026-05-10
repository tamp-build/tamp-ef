using Xunit;

namespace Tamp.EFCore.V9.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Assembly_Loads_And_EFCore_Type_Is_Reachable()
    {
        Assert.NotNull(typeof(EFCore));
    }
}
