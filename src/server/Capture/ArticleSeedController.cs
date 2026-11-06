using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Data.Core.Model;
using Koan.Identity.Roles;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;

namespace Tangent.Capture;

/// <summary>The rows the article-editor and article captures show, sent as commands when the capture asks: an
/// articles Tangent holding the persona's article with a cover, a heading, a list, a quote and an image with its caption,
/// and two more by other people, one without a cover. The pictures are drawn here and kept as any image is. Compiled
/// only into the capture host; fixed names, words, times and TIDs, so the paths in
/// <c>src/client/capture/screens.ts</c> name these Posts.</summary>
public sealed class ArticleSeedController(RoleCollection roles) : ControllerBase
{
    private const string Persona = Seeded.Persona;
    private const string Leo = Seeded.Leo;
    private const string Mara = Seeded.Mara;

    [HttpPost("/capture/seed/article")]
    public async Task<IActionResult> Seed(CancellationToken ct)
    {
        var space = await TangentSpace.Get(TangentSpace.SingleRow, ct) ?? throw new InvalidOperationException("The Tangent Space row is missing.");
        space.Name = "Kintsugi Architecture";
        await Write(new Keep<TangentSpace>(space), ct);

        await Person(Leo, "leo.sylin.org", "Leo Botinelly", Roles.Administrator, ct);
        await Person(Mara, "mara.bsky.social", "Mara Lind", Roles.Member, ct);
        await Person(Persona, "ada.bsky.social", "Ada Okafor", Roles.Member, ct);

        await Write(new CreateTangent("articles", "Articles", "Long pieces on repair, systems and the things that outlast them.", PostKind.Article, 1, Audience.Public, Leo), ct);

        var cover = await Brought(Bowl(2000, 1125, 0.5, 0.52, 0.31), Persona, ct);
        var inline = await Brought(Bowl(1360, 900, 0.34, 0.5, 0.26), Persona, ct);
        var second = await Brought(Bowl(1400, 900, 0.72, 0.4, 0.42), Mara, ct);

        // Her article is 3mwgqaw5is227 at /t/articles/mending-with-gold.
        await Write(new StartTopic(Tid(At(26, 16, 45), 5), "articles", "Mending with gold", $"""
            A cracked bowl tells you where it was weak. *Kintsugi* does not hide that: the seam is filled with lacquer and dusted with gold, so the repair becomes the part you look at first.

            ## What the seam remembers

            Most software repairs try to leave no trace. The patch lands, the changelog says "fixed", and six months later nobody recalls why that branch exists. A visible seam keeps three things in reach:

            - where the thing broke,
            - what held it together afterwards,
            - and who cared enough to mend it.

            > The break is part of the history of the object, not something to disguise.

            ![A bowl on the bench, its seam catching the light]({Media.Reference(inline.Id)})

            *The second firing: the lacquer has set, and the gold goes on last.*

            ### Three habits worth keeping

            1. Write the repair down where the repair is.
            2. Name [what you learned](https://example.com/notes), not only what you changed.
            3. Leave the seam where the next person can see it.

            None of this makes the bowl new. It makes it **yours**, and it will hold soup again by the weekend.
            """.Replace("\n            ", "\n"), Persona, At: At(26, 16, 45), CoverId: cover.Id), ct);

        await Write(new StartTopic(Tid(At(24, 9, 10), 6), "articles", "The lacquer takes its time",
            "Urushi cures in damp air, not dry. A workshop that hurries it gets a seam that looks finished and peels by spring.", Mara, At: At(24, 9, 10), CoverId: second.Id), ct);
        await Write(new StartTopic(Tid(At(21, 14, 30), 7), "articles", "Why I keep the broken ones",
            "A shelf of cracked things is a better teacher than a shelf of whole ones.", Leo, At: At(21, 14, 30)), ct);
        return NoContent();
    }

    private async Task<Media> Brought(byte[] picture, string by, CancellationToken ct)
    {
        var media = await Media.Keep(picture, ct);
        await Write(new BringMedia(media.Id, by), ct);
        return media;
    }

    /// <summary>A dark glazed bowl on a warm ground, a gold seam across it, seen from above; fixed by its arguments.</summary>
    internal static byte[] Bowl(int width, int height, double centre, double middle, double reach)
    {
        using var image = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(width, height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque));
        var (cx, cy, radius) = (width * centre, height * middle, Math.Min(width, height) * reach * 1.25);
        var seam = Enumerable.Range(0, width).Select(x => cy + Math.Sin(x * 0.011) * radius * 0.18 + Math.Sin(x * 0.037 + 1.3) * radius * 0.06).ToArray();
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var (dx, dy) = (x - cx, y - cy);
                    var distance = Math.Sqrt(dx * dx + dy * dy) / radius;
                    var grain = Math.Sin(x * 0.05 + Math.Sin(y * 0.013) * 4) * 0.012;
                    double r, g, b;
                    if (distance > 1)
                    {
                        // The bench, darker under the bowl's shadow and toward the corners.
                        var light = 0.82 - 0.26 * ((double)y / height) + grain - Math.Max(0, 0.22 - (distance - 1) * 0.6);
                        (r, g, b) = (light * 0.86, light * 0.74, light * 0.58);
                    }
                    else
                    {
                        // The glaze: a deep blue-black with a soft highlight, a lighter well at the centre.
                        var well = Math.Max(0, 1 - distance / 0.62);
                        var sheen = Math.Max(0, 1 - Math.Sqrt(Math.Pow(dx + radius * 0.34, 2) + Math.Pow(dy + radius * 0.38, 2)) / (radius * 0.5));
                        var shade = 0.13 + 0.09 * well + 0.3 * sheen * sheen + grain;
                        (r, g, b) = (shade * 0.82, shade * 0.96, shade * 1.14);
                        var along = Math.Abs(y - seam[x]);
                        var width0 = 2.6 + 2.4 * Math.Abs(Math.Sin(x * 0.021));
                        if (along < width0)
                        {
                            var gold = 1 - along / width0;
                            (r, g, b) = (r + gold * 0.82, g + gold * 0.6, b + gold * 0.2);
                        }
                    }

                    image.SetPixel(x, y, new SkiaSharp.SKColor(Byte(r), Byte(g), Byte(b)));
                }
            }
        }
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 75);
        return encoded.ToArray();
    }

    private static byte Byte(double value) => (byte)Math.Clamp(value * 255, 0, 255);

    private async Task Person(string id, string handle, string displayName, string role, CancellationToken ct)
    {
        await Write(new Keep<Participant>(new Participant { Id = id, Did = Seeded.Did(id), Handle = handle }), ct);
        await Write(new CaptureProfile(id, new ProfileAnswer(Seeded.Did(id), displayName, null, null, AgentLabel: false), DateTimeOffset.UtcNow), ct);
        await roles.Add(role, id, ct);
    }

    private async Task<T> Write<T>(ICommand<T> command, CancellationToken ct) => await command.Send() switch
    {
        Outcome<T>.Done done => done.Result,
        var other => throw new InvalidOperationException($"The article seed's {command.GetType().Name} answered {other}.")
    };

    /// <summary>A time in September 2026, in UTC.</summary>
    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>The TID a client would mint at this time with this clock id.</summary>
    private static string Tid(DateTimeOffset at, int clock)
    {
        var value = ((at - DateTimeOffset.UnixEpoch).Ticks / 10 << 10) | (uint)clock;
        var characters = new char[Posts.Tid.Length];
        for (var position = Posts.Tid.Length - 1; position >= 0; position--)
        {
            characters[position] = Posts.Tid.Alphabet[(int)(value & 31)];
            value >>= 5;
        }

        return new string(characters);
    }

    private sealed record Keep<TEntity>(TEntity Row) : ICommand<TEntity> where TEntity : Entity<TEntity>
    {
        public async Task<Outcome<TEntity>> Run(CancellationToken ct)
        {
            await Row.Save(ct);
            return new Outcome<TEntity>.Done(Row);
        }
    }
}
