using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

public interface IDirectoryReader
{
    Task<IReadOnlyList<AppRegistration>> GetAppRegistrationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Every group, with whether it has direct members and owners.</summary>
    Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
