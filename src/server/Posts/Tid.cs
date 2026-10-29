namespace Tangent.Posts;

/// <summary>A Post's Id as the client mints it: an atproto TID, thirteen characters of the sortable base32 alphabet
/// encoding a 63-bit value. The server mints none and accepts only this shape.</summary>
public static class Tid
{
    public const string Alphabet = "234567abcdefghijklmnopqrstuvwxyz";

    public const int Length = 13;

    public static bool IsValid(string? id)
    {
        if (id is null || id.Length != Length) return false;
        // The first character carries the high bit, which is always zero.
        if (Alphabet.IndexOf(id[0]) is < 0 or > 15) return false;
        for (var position = 1; position < Length; position++)
        {
            if (Alphabet.IndexOf(id[position]) < 0) return false;
        }

        return true;
    }
}
