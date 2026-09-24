using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Entraspect.Core.Tests;

internal sealed class FakeCheck : ICheck
{
    private readonly System.Func<IDirectoryReader, CheckOptions, CancellationToken, Task<IReadOnlyList<Finding>>>? _run;

    public FakeCheck(
        string id,
        string? title = null,
        CheckCategory category = CheckCategory.AppRegistrations,
        IReadOnlyList<string>? requiredPermissions = null,
        System.Func<IDirectoryReader, CheckOptions, CancellationToken, Task<IReadOnlyList<Finding>>>? run = null)
    {
        Id = id;
        Title = title ?? id;
        Description = id;
        Category = category;
        RequiredPermissions = requiredPermissions ?? System.Array.Empty<string>();
        _run = run;
    }

    public string Id { get; }

    public string Title { get; }

    public string Description { get; }

    public CheckCategory Category { get; }

    public IReadOnlyList<string> RequiredPermissions { get; }

    public Task<IReadOnlyList<Finding>> RunAsync(
        IDirectoryReader reader,
        CheckOptions options,
        CancellationToken cancellationToken = default) =>
        _run is null
            ? Task.FromResult<IReadOnlyList<Finding>>(System.Array.Empty<Finding>())
            : _run(reader, options, cancellationToken);
}
