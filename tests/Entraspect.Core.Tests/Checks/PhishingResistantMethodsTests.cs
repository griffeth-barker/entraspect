using Entraspect.Core.Checks;

namespace Entraspect.Core.Tests.Checks;

public sealed class PhishingResistantMethodsTests
{
    [Theory]
    [InlineData("passKeyDeviceBound")]
    [InlineData("passKeyDeviceBoundAuthenticator")]
    [InlineData("passKeyDeviceBoundWindowsHello")]
    [InlineData("fido2SecurityKey")]
    [InlineData("fido2")]
    [InlineData("windowsHelloForBusiness")]
    [InlineData("macOsSecureEnclaveKey")]
    [InlineData("certificateBasedAuthentication")]
    [InlineData("PASSKEYDEVICEBOUND")]
    [InlineData("WindowsHelloForBusiness")]
    public void Phishing_resistant_methods_match_ignoring_case(string method)
    {
        Assert.True(PhishingResistantMethods.AnyIn([method]));
    }

    [Theory]
    [InlineData("microsoftAuthenticatorPush")]
    [InlineData("microsoftAuthenticatorPasswordless")]
    [InlineData("softwareOneTimePasscode")]
    [InlineData("hardwareOneTimePasscode")]
    [InlineData("mobilePhone")]
    [InlineData("email")]
    [InlineData("temporaryAccessPass")]
    [InlineData("passKeySynced")]
    [InlineData("somethingNew")]
    [InlineData("")]
    public void Other_and_unknown_methods_are_not_phishing_resistant(string method)
    {
        Assert.False(PhishingResistantMethods.AnyIn([method]));
    }

    [Fact]
    public void One_phishing_resistant_method_among_others_is_enough()
    {
        Assert.True(PhishingResistantMethods.AnyIn(["mobilePhone", "fido2SecurityKey", "email"]));
        Assert.False(PhishingResistantMethods.AnyIn([]));
    }
}
