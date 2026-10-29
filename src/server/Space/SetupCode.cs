using System.Security.Cryptography;
using System.Text;

namespace Tangent.Space;

/// <summary>The one-time code that lets the person who runs the server set up a Tangent Space whose configuration
/// names no operator: a start that finds the place unestablished draws a new one and writes it to the log, and the
/// first-run form asks for it. It lives only in the process, so it changes at every start and is never shown in a
/// page or its JSON.</summary>
public sealed class SetupCode
{
    /// <summary>Crockford's base32 alphabet: no I, L, O or U to misread.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private const int Length = 16;

    private byte[]? _code;

    /// <summary>Whether a code was drawn: a start found the place unestablished with no operator configured.</summary>
    public bool Required => _code is not null;

    /// <summary>Draws a new code and answers it, in four groups of four, as the log shows it.</summary>
    public string Draw()
    {
        var characters = new char[Length];
        for (var index = 0; index < Length; index++) characters[index] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        _code = Encoding.ASCII.GetBytes(characters);
        return string.Join('-', Enumerable.Range(0, Length / 4).Select(group => new string(characters, group * 4, 4)));
    }

    /// <summary>Whether what was typed is the code, ignoring case, spaces and hyphens; compared in constant time.</summary>
    public bool Matches(string? typed)
    {
        if (_code is null || typed is null) return false;
        var written = new string(typed.Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());
        return written.Length == Length && written.All(c => c < 128)
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(written), _code);
    }
}
