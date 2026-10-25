namespace Entraspect.Core;

/// <summary>
/// A directory role, built-in or custom. Role assignments name built-in roles by <see cref="TemplateId"/>, which for
/// built-in roles equals <see cref="Id"/>; custom roles may have no template ID.
/// </summary>
public sealed class DirectoryRoleDefinition
{
    public required string Id { get; init; }

    public string? TemplateId { get; init; }

    /// <summary>The role's name, or null when Graph left it out.</summary>
    public string? DisplayName { get; init; }
}
