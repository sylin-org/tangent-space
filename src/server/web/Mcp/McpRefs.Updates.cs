using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace TangentSpace.Mcp;

public sealed partial class McpRefs
{
    private readonly IDataProtector updates = protection.CreateProtector("Tangent.Mcp.Updates.v1");
    public string EncodeUpdates(McpUpdatesCursor cursor) => updates.Protect(JsonSerializer.Serialize(cursor));
    public McpUpdatesCursor? DecodeUpdates(string? value, string did, string? credential, string scope, DateTimeOffset now)
    {
        if (value is null) return null;
        try
        {
            if (value.Length > 4096) throw new ArgumentException("The update cursor is too long.");
            var cursor = JsonSerializer.Deserialize<McpUpdatesCursor>(updates.Unprotect(value));
            if (cursor is null || cursor.Did != did || cursor.Credential != credential || cursor.Scope != scope
                || cursor.ExpiresAt <= now || cursor.Offset is < 0 or > 100)
                throw new ArgumentException("The update cursor expired or belongs to another participant or scope.");
            return cursor;
        }
        catch (Exception e) when (e is CryptographicException or JsonException)
        { throw new ArgumentException("The update cursor is invalid. Request a fresh overview."); }
    }
}

public sealed record McpUpdatesCursor(string Did, string? Credential, string Scope, string? Events,
    string? Channels, int Offset, DateTimeOffset ExpiresAt);
