using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace Entraspect.Graph.Tests;

public sealed class JwtRoleParserTests
{
    private static string Encode(string json, bool pad)
    {
        var s = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-')
            .Replace('/', '_');
        return pad ? s : s.TrimEnd('=');
    }

    private static string TokenWith(string payloadJson, bool pad = false) =>
        $"header.{Encode(payloadJson, pad)}.signature";

    [Fact]
    public void Roles_present_are_returned()
    {
        var token = TokenWith("{\"roles\":[\"Application.Read.All\",\"Directory.Read.All\"]}");

        var roles = JwtRoleParser.ParseRoles(token);

        Assert.Equal(new[] { "Application.Read.All", "Directory.Read.All" }, roles);
    }

    [Fact]
    public void Roles_absent_returns_empty()
    {
        var token = TokenWith("{\"sub\":\"abc123\"}");

        Assert.Empty(JwtRoleParser.ParseRoles(token));
    }

    [Fact]
    public void Roles_empty_array_returns_empty()
    {
        var token = TokenWith("{\"roles\":[]}");

        Assert.Empty(JwtRoleParser.ParseRoles(token));
    }

    [Fact]
    public void Null_or_empty_token_returns_empty()
    {
        Assert.Empty(JwtRoleParser.ParseRoles(null));
        Assert.Empty(JwtRoleParser.ParseRoles(string.Empty));
        Assert.Empty(JwtRoleParser.ParseRoles("   "));
    }

    [Fact]
    public void Token_without_dots_is_malformed()
    {
        Assert.Empty(JwtRoleParser.ParseRoles("this is not a jwt"));
    }

    [Fact]
    public void Payload_that_is_not_base64_is_malformed()
    {
        var token = "header.!!!not-base64!!!.signature";

        Assert.Empty(JwtRoleParser.ParseRoles(token));
    }

    [Fact]
    public void Payload_that_is_not_json_is_malformed()
    {
        var token = TokenWith("hello, this is not json");

        Assert.Empty(JwtRoleParser.ParseRoles(token));
    }

    [Fact]
    public void Base64url_payload_with_padding_is_decoded()
    {
        var token = TokenWith("{\"roles\":[\"Application.Read.All\"]}", pad: true);

        Assert.Equal(new[] { "Application.Read.All" }, JwtRoleParser.ParseRoles(token));
    }
}
