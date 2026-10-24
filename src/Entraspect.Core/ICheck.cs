using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core;

public interface ICheck
{
    string Id { get; }

    string Title { get; }

    string Description { get; }

    CheckCategory Category { get; }

    IReadOnlyList<string> RequiredPermissions { get; }

    /// <summary>Column headers for this check's findings. Defaults to generic labels.</summary>
    FindingLabels Labels => FindingLabels.Default;

    Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default);
}
