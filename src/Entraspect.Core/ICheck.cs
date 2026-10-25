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
    /// Runs the check and reports how far it is as short text, for example "looking up user 4 of 20". A check that
    /// makes many reads implements it, and the others run <see cref="RunAsync(IDirectoryReader, CheckOptions,
    /// CancellationToken)"/> without progress.
    /// </summary>
    Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        RunAsync(reader, options, cancellationToken);

    /// <summary>
    /// Runs the check for a scan: its findings and, for a check that reads a period of logs, which period and how many
    /// entries it read. The scan calls this one; the default runs <see cref="RunAsync(IDirectoryReader, CheckOptions,
    /// IProgress{string}?, CancellationToken)"/> and has no period.
    /// </summary>
    async Task<CheckRun> ScanAsync(
        IDirectoryReader reader,
        CheckOptions options,
        IProgress<string>? progress,
        CancellationToken cancellationToken) =>
        new(await RunAsync(reader, options, progress, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// When the data this check reads was last refreshed, for checks that read a report Microsoft updates on its own
    /// schedule; null otherwise. Called after <see cref="RunAsync"/> with the same reader, so it reads cached data.
    /// </summary>
    Task<DateTimeOffset?> GetDataUpdatedAsync(
        IDirectoryReader reader,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTimeOffset?>(null);
}
