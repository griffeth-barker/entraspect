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

    /// <summary>The licence the tenant needs for this check. A tenant without it doesn't run the check.</summary>
    LicenceRequirement RequiredLicence => LicenceRequirement.None;

    /// <summary>Column headers for this check's findings. Defaults to generic labels.</summary>
    FindingLabels Labels => FindingLabels.Default;

    Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// When the data this check reads was last refreshed, for checks that read a report Microsoft updates on its own
    /// schedule; null otherwise. Called after <see cref="RunAsync"/> with the same reader, so it reads cached data.
    /// </summary>
    Task<DateTimeOffset?> GetDataUpdatedAsync(
        IDirectoryReader reader,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTimeOffset?>(null);
}
