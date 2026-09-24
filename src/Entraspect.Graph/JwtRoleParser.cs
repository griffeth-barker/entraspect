using System.Text;
using System.Text.Json;

namespace Entraspect.Graph;

/// <summary>
/// Extracts the application permissions granted to a token from its <c>roles</c> claim, decoding only the JWT payload.
/// The signature is never validated: the result is only used to compare against the permissions a check needs.
/// A token with no <c>roles</c> claim, or one that cannot be decoded, means no permissions are granted.
/// </summary>
internal static class JwtRoleParser
{
    public static IReadOnlyList<string> ParseRoles(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Array.Empty<string>();
        }

        var parts = token.Split('.');
        if (parts.Length < 2)
        {
            return Array.Empty<string>();
        }

        try
        {
            var json = Base64UrlDecode(parts[1]);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("roles", out var roles) &&
                roles.ValueKind == JsonValueKind.Array)
            {
                var result = new List<string>();
                foreach (var role in roles.EnumerateArray())
                {
                    if (role.ValueKind == JsonValueKind.String)
                    {
                        result.Add(role.GetString()!);
                    }
                }

                return result;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or JsonException or ArgumentException)
        {
            // Malformed token or payload: treat as no granted permissions.
        }

        return Array.Empty<string>();
    }

    private static string Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: s += "==="; break;
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(s));
    }
}
