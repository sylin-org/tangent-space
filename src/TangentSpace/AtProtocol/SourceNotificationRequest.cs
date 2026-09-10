using System.Text.RegularExpressions;
using CarpaNet.Identity;
using Newtonsoft.Json.Linq;

namespace TangentSpace.AtProtocol;

public sealed record SourceNotificationRequest(string Space, string Repo, string Rev, JToken? Hash)
{
    public bool TryValidate(SpacesOptions options, out string room, out string hash)
    {
        room = ""; hash = "";
        var prefix = options.Space("");
        if (Space is null || !Space.StartsWith(prefix, StringComparison.Ordinal) || Space.Length > 2048
            || Repo is null || Repo.Length > 2048 || !IdentityResolver.IsValidDid(Repo)
            || Rev is null || !Regex.IsMatch(Rev, "^[234567abcdefghijklmnopqrstuvwxyz]{13}$", RegexOptions.CultureInvariant)) return false;
        room = Space[prefix.Length..];
        try { Rooms.Room.CheckKey(room); } catch (Rooms.RoomRuleViolation) { return false; }
        string? encoded = null;
        // Koan MVC uses Newtonsoft at the HTTP boundary. JsonElement would bind as
        // Undefined here even for a valid native Lexicon bytes object.
        if (Hash is JObject { Count: 1 } obj && obj.TryGetValue("$bytes", out var bytes)
            && bytes.Type == JTokenType.String) encoded = bytes.Value<string>();
        if (encoded is null || !Regex.IsMatch(encoded, "^[A-Za-z0-9+/]{43}=?$", RegexOptions.CultureInvariant)) return false;
        try
        {
            // Lexicon JSON uses unpadded base64; accept its padded equivalent as well.
            var decoded = Convert.FromBase64String(encoded.PadRight(44, '='));
            if (decoded.Length != 32) return false;
            hash = Convert.ToBase64String(decoded);
            if (!string.Equals(hash.TrimEnd('='), encoded.TrimEnd('='), StringComparison.Ordinal)) return false;
            return true;
        }
        catch (FormatException) { return false; }
    }
}
