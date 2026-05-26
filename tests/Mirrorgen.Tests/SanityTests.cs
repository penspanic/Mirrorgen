using Mirrorgen.Core;
using Xunit;

namespace Mirrorgen.Tests;

public class SanityTests
{
    [Fact]
    public void Engine_Version_Is_NonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(TranspilerEngine.Version));
    }

    [Fact]
    public void TranspileAttribute_Default_EmitName_Is_Null()
    {
        var attr = new TranspileAttribute();
        Assert.Null(attr.EmitName);
    }
}
