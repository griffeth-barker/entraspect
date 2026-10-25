namespace Entraspect.Core.Tests;

public sealed class CheckFailureMessageTests
{
    [Theory]
    [InlineData("Authentication_RequestFromNonPremiumTenantOrB2CTenant")]
    [InlineData("authentication_requestfromnonpremiumtenantorb2ctenant")]
    public void Non_premium_tenant_error_code_gives_the_licence_message(string errorCode)
    {
        var result = Failed("Microsoft Graph returned HTTP 403: Tenant does not have a SKU required.", errorCode);

        Assert.True(CheckFailureMessage.IsLicenceRequired(errorCode));
        Assert.Equal("This check needs an Entra ID P1 or P2 licence.", CheckFailureMessage.For(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Authorization_RequestDenied")]
    [InlineData("Request_ResourceNotFound")]
    public void Other_error_codes_keep_the_Graph_error(string? errorCode)
    {
        var result = Failed("Microsoft Graph returned HTTP 403: Insufficient privileges.", errorCode);

        Assert.False(CheckFailureMessage.IsLicenceRequired(errorCode));
        Assert.Equal("Microsoft Graph returned HTTP 403: Insufficient privileges.", CheckFailureMessage.For(result));
    }

    [Fact]
    public void Missing_error_message_is_unknown_error()
    {
        Assert.Equal("Unknown error.", CheckFailureMessage.For(Failed(null, null)));
    }

    private static CheckResult Failed(string? message, string? errorCode) => new()
    {
        CheckId = "c1",
        Status = CheckResultStatus.Failed,
        ErrorMessage = message,
        StatusCode = 403,
        ErrorCode = errorCode,
    };
}
