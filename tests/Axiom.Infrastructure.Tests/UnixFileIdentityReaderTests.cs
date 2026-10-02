namespace Axiom.Infrastructure.Tests;

public sealed class UnixFileIdentityReaderTests
{
    [Theory]
    [InlineData("00012:0042\n", "12:42")]
    [InlineData("18446744073709551615:18446744073709551615", "18446744073709551615:18446744073709551615")]
    public void Device_Inode_Parsing_Normalizes_Numeric_Identity(string output, string expected)
    {
        Assert.Equal(expected, UnixFileIdentityReader.ParseIdentity(output));
    }

    [Theory]
    [InlineData("12:0")]
    [InlineData("-1:2")]
    [InlineData("12:2:3")]
    [InlineData("file path:secret")]
    [InlineData("18446744073709551616:1")]
    public void Rejects_Malformed_Or_Unusable_Identity(string output)
    {
        Assert.Throws<InvalidOperationException>(() => UnixFileIdentityReader.ParseIdentity(output));
    }
}
