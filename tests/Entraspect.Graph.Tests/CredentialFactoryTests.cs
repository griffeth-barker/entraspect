namespace Entraspect.Graph.Tests;

public sealed class CredentialFactoryTests
{
    [Theory]
    [InlineData("abcdef0123456789abcdef0123456789abcdef01")]
    [InlineData("‎ABCDEF0123456789ABCDEF0123456789ABCDEF01")]
    [InlineData("ab cd ef 01 23 45 67 89 ab cd ef 01 23 45 67 89 ab cd ef 01")]
    [InlineData("AB:CD:EF:01:23:45:67:89:AB:CD:EF:01:23:45:67:89:AB:CD:EF:01")]
    public void NormalizeThumbprint_strips_separators_and_uppercases(string input)
    {
        Assert.Equal("ABCDEF0123456789ABCDEF0123456789ABCDEF01", CredentialFactory.NormalizeThumbprint(input));
    }
}
