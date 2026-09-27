using Axiom.Core;

namespace Axiom.Core.Tests;

public sealed class ApplicationBuildIdentityTests
{
    [Fact]
    public void ParseSeparatesProductVersionAndBuildProvenance()
    {
        var identity = ApplicationBuildIdentity.Parse("2.3.4-preview.2+abcdef123456", new Version(9, 9, 9));

        Assert.Equal("2.3.4-preview.2", identity.ProductVersion);
        Assert.Equal("abcdef123456", identity.Provenance);
        Assert.Equal("Axiom 2.3.4-preview.2 (abcdef123456)", identity.DisplayText);
    }

    [Fact]
    public void ParseUsesExplicitDevelopmentFallbackWithoutInformationalMetadata()
    {
        var identity = ApplicationBuildIdentity.Parse(null, new Version(7, 8, 9, 0));

        Assert.Equal("7.8.9", identity.ProductVersion);
        Assert.Equal("dev", identity.Provenance);
        Assert.Equal("7.8.9+dev", identity.InformationalVersion);
    }
}
