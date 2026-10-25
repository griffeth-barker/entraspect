using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

public interface IDirectoryReader
{
    Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every group, with whether it has direct members and owners.</summary>
    Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every enterprise app (service principal), with its token signing certificates.</summary>
    Task<IReadOnlyList<EnterpriseApp>> GetEnterpriseAppsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Users' MFA registration from the user registration details report. An implementation may leave out users who
    /// are MFA capable; callers must still test <see cref="UserRegistration.IsMfaCapable"/>.
    /// </summary>
    Task<IReadOnlyList<UserRegistration>> GetUserRegistrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every Microsoft Graph application permission granted to a service principal in the tenant.</summary>
    Task<IReadOnlyList<AppPermissionGrant>> GetGraphApplicationPermissionGrantsAsync(
        CancellationToken cancellationToken = default);
}
