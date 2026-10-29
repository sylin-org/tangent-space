namespace Tangent.Posts;

/// <summary>Mints TIDs for the tests, the way a client does: microseconds since the epoch in the high 53 bits, a clock
/// id in the low 10, each one higher than the last this process minted.</summary>
public static class Tids
{
    private static readonly int Clock = Random.Shared.Next(0, 1024);
    private static long _last;

    public static string Mint()
    {
        long micros;
        long previous;
        do
        {
            previous = Volatile.Read(ref _last);
            micros = Math.Max((DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks / 10, previous + 1);
        } while (Interlocked.CompareExchange(ref _last, micros, previous) != previous);

        var value = (micros << 10) | (uint)Clock;
        var characters = new char[Tid.Length];
        for (var position = Tid.Length - 1; position >= 0; position--)
        {
            characters[position] = Tid.Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(characters);
    }
}
