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

    [Fact]
    public void Create_with_missing_pfx_file_throws_clear_error()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.pfx");
        var profile = new ConnectionProfile
        {
            Name = "test",
            TenantId = "contoso.onmicrosoft.com",
            ClientId = "00000000-0000-0000-0000-000000000000",
            AuthMode = AuthMode.AppOnly,
            CertificatePath = path,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => CredentialFactory.Create(profile));

        Assert.Contains("was not found", ex.Message);
        Assert.Contains(path, ex.Message);
    }
}
