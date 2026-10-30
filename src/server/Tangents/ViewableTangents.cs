using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Tangent.Access;
using Tangent.Posts;

namespace Tangent.Tangents;

/// <summary>The Tangents a reader may view, or those where they hold a verb, in the Tangents' order. The Tangents are
/// read <see cref="ReadSize"/> at a time, each read continuing by seek from the stored order and slug of the last
/// Tangent the one before read (<see cref="Tangent.InOrder"/>), and each Tangent is judged from Koan's compiled state for
/// its resource (<see cref="Capabilities.LoadEach"/>). A page keeps what it asks for, each Tangent once, and continues
/// from a <see cref="Cursor"/> naming the last Tangent it keeps, sealed so the reader holds it without reading it and
/// cannot forge one (<see cref="Cursor.Seal"/>): nothing about a Tangent the reader may not view is said, not on a page,
/// not by its length, not by a cursor. The reading stops once the page is full and one more is found, so a page costs
/// the Tangents read up to that one: at most every Tangent the place holds, the place's own size. A Tangent created or
/// moved while the reading goes on can be read twice, which is kept once, or missed. Reads only and takes no
/// lock.</summary>
public static class ViewableTangents
{
    /// <summary>The most Tangents one read takes.</summary>
    public const int ReadSize = 100;

    /// <summary>The purpose a cursor is sealed under, which no other protected value shares.</summary>
    public const string CursorPurpose = "Tangent.Cursor";

    /// <summary>The place of the last Tangent a page kept: its order and its slug. It leaves the server only sealed.</summary>
    public sealed record Cursor(int Order, string Id)
    {
        public static Cursor After(Tangent tangent) => new(tangent.Order, tangent.Id);

        /// <summary>The cursor as an address carries it: protected under <see cref="CursorPurpose"/>, so it says nothing
        /// of the order it holds and is never accepted altered.</summary>
        public string Seal(IDataProtectionProvider protection)
            => protection.CreateProtector(CursorPurpose).Protect(Order.ToString(CultureInfo.InvariantCulture) + "." + Id);

        /// <summary>The cursor a sealed text holds, or null when the text is not one this server sealed.</summary>
        public static Cursor? Open(string? text, IDataProtectionProvider protection)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string plain;
            try { plain = protection.CreateProtector(CursorPurpose).Unprotect(text); }
            catch (CryptographicException) { return null; }
            var dot = plain.IndexOf('.');
            if (dot <= 0) return null;
            if (!int.TryParse(plain[..dot], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var order)) return null;
            var id = plain[(dot + 1)..];
            return Slug.IsValid(id) ? new Cursor(order, id) : null;
        }
    }

    /// <summary>A page: the Tangents kept, in order, what the reader holds on each, by slug, and where the next page
    /// starts, null on the last.</summary>
    public sealed record Page(IReadOnlyList<Tangent> Tangents, IReadOnlyDictionary<string, Capabilities> Each, Cursor? Next);

    /// <summary>Every Tangent the reader may view: <see cref="Tangents"/> in order, and <see cref="Others"/>, the slugs of
    /// the rest, which a read of Posts may name to leave them out.</summary>
    public sealed record Whole(IReadOnlyList<Tangent> Tangents, IReadOnlyList<string> Others);

    /// <summary>Whether the reader may view the Tangent.</summary>
    public static bool Views(Capabilities capabilities) => capabilities.Exists;

    /// <summary>The rows of one read that no earlier read of the same reading returned, by slug, marking them read.</summary>
    public static IReadOnlyList<Tangent> Fresh(IReadOnlyList<Tangent> rows, ISet<string> read) => rows.Where(row => read.Add(row.Id)).ToList();

    /// <summary>The page of at most <paramref name="take"/> Tangents where <paramref name="keep"/> holds for the reader,
    /// after <paramref name="after"/>, or the first page.</summary>
    public static async Task<Page> Read(string? personId, Func<Capabilities, bool> keep, Cursor? after, int take, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var kept = new List<Tangent>();
        var each = new Dictionary<string, Capabilities>(StringComparer.Ordinal);
        var read = new HashSet<string>(StringComparer.Ordinal);
        (int Order, string Id)? from = after is null ? null : (after.Order, after.Id);
        while (true)
        {
            var rows = await Tangent.InOrder(from, earlier: false, aside: null, ReadSize, ct);
            var fresh = Fresh(rows, read);
            var judged = await Capabilities.LoadEach(personId, fresh.Select(row => row.Id).ToList(), ct);
            foreach (var row in fresh)
            {
                if (!keep(judged[row.Id])) continue;
                if (kept.Count == take) return new Page(kept, each, Cursor.After(kept[^1]));
                kept.Add(row);
                each[row.Id] = judged[row.Id];
            }

            if (rows.Count < ReadSize) return new Page(kept, each, null);
            from = (rows[^1].Order, rows[^1].Id);
        }
    }

    /// <summary>Every Tangent the reader may view, and the slugs of the others: every Tangent read once.</summary>
    public static async Task<Whole> All(string? personId, CancellationToken ct)
    {
        var kept = new List<Tangent>();
        var others = new List<string>();
        var read = new HashSet<string>(StringComparer.Ordinal);
        (int Order, string Id)? from = null;
        while (true)
        {
            var rows = await Tangent.InOrder(from, earlier: false, aside: null, ReadSize, ct);
            var fresh = Fresh(rows, read);
            var judged = await Capabilities.LoadEach(personId, fresh.Select(row => row.Id).ToList(), ct);
            foreach (var row in fresh)
            {
                if (Views(judged[row.Id])) kept.Add(row);
                else others.Add(row.Id);
            }

            if (rows.Count < ReadSize) return new Whole(kept, others);
            from = (rows[^1].Order, rows[^1].Id);
        }
    }
}
