using System.Security.Cryptography;
using System.Text;

namespace Tangent.Posts;

/// <summary>A Post's Id as the client mints it: an atproto TID, thirteen characters of the sortable base32 alphabet
/// encoding a 63-bit value. Commands accept only this shape and mint none; a form the server renders
/// carries one minted for it (<see cref="Mint"/>), so a browser without script sends an Id like any other client, and a
/// write that sends none goes by one its words name (<see cref="Derived"/>).</summary>
public static class Tid
{
    public const string Alphabet = "234567abcdefghijklmnopqrstuvwxyz";

    public const int Length = 13;

    /// <summary>How long words name one Id: a retry within this of the words that wrote a Post is that Post, whichever
    /// window it falls in, and the same words later than twice this are a new one.</summary>
    public static readonly TimeSpan RetryWindow = TimeSpan.FromMinutes(5);

    /// <summary>The clock id in the low ten bits of every TID this process mints.</summary>
    private static readonly int Clock = Random.Shared.Next(0, 1024);

    private static long _last;

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

    /// <summary>A TID for a form being rendered at <paramref name="now"/>: microseconds since the epoch in the high
    /// bits and this process's clock id in the low ten, each one higher than the last this process minted.</summary>
    public static string Mint(DateTimeOffset now)
    {
        long micros;
        long previous;
        do
        {
            previous = Volatile.Read(ref _last);
            micros = Math.Max((now - DateTimeOffset.UnixEpoch).Ticks / 10, previous + 1);
        } while (Interlocked.CompareExchange(ref _last, micros, previous) != previous);

        return Encoded((micros << 10) | (uint)Clock);
    }

    /// <summary>The TID <paramref name="words"/> name at <paramref name="at"/>: the same within one
    /// <see cref="RetryWindow"/>, and another in the next, its time within the window and its clock id both from the
    /// words' hash.</summary>
    public static string Derived(string words, DateTimeOffset at)
    {
        var window = RetryWindow.Ticks / 10;
        var hash = BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(words)));
        var micros = (at - DateTimeOffset.UnixEpoch).Ticks / 10;
        return Encoded(((micros - micros % window + (long)(hash % (ulong)window)) << 10) | (long)(hash >> 54));
    }

    private static string Encoded(long value)
    {
        var characters = new char[Length];
        for (var position = Length - 1; position >= 0; position--)
        {
            characters[position] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(characters);
    }
}
