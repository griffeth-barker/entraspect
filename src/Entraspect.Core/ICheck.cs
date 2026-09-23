using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

public interface ICheck
{
    string Id { get; }

    string Title { get; }

    string Description { get; }

    IReadOnlyList<string> RequiredPermissions { get; }

    Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default);
}
