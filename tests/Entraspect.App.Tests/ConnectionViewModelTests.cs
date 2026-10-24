using System.IO;
using System.Threading.Tasks;
using Entraspect.App.Services;
using Entraspect.App.ViewModels;

namespace Entraspect.App.Tests;

public sealed class ConnectionViewModelTests
{
    private static string TempDir() => Path.Combine(Path.GetTempPath(), $"entraspect-connection-{Guid.NewGuid()}");

    [Fact]
    public void Saving_a_profile_never_writes_the_typed_password()
    {
        const string password = "fake-pfx-password-for-test";
        var dir = TempDir();
        try
        {
            var vm = new ConnectionViewModel((_, _) => Task.CompletedTask, new ProfileStore(dir))
            {
                Name = "p1",
                TenantId = "t1",
                ClientId = "c1",
                CertificatePath = "/x.pfx",
                Password = password,
            };

            vm.SaveProfileCommand.Execute(null);

            var text = File.ReadAllText(Path.Combine(dir, "profiles.json"));
            Assert.Contains("/x.pfx", text);
            Assert.DoesNotContain(password, text);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
