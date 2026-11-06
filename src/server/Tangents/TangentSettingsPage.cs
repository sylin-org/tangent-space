using Tangent.Render;

namespace Tangent.Tangents;

/// <summary>A Tangent's settings, one resource both ways: its <see cref="Slug"/>, which never changes, and what may:
/// its <see cref="Name"/>, <see cref="Description"/>, its <see cref="Accent"/>, empty for the <see cref="Tone"/> its slug gives
/// it, its <see cref="Image"/>, empty or one of the <see cref="Covers"/> or an image brought in here, default <see cref="Kind"/> (one of <see cref="Kinds"/>), its
/// <see cref="Audience"/> (one of <see cref="Audiences"/>, which is empty where the audience is fixed) and, for a
/// reader who <see cref="Movable"/> it — who holds <c>ManageSpace</c>, since a move arranges the place's list — its place
/// among the Tangents, between <see cref="Before"/> and <see cref="After"/>, the names of the nearest Tangents that
/// reader may view, null at either end and for every other reader. <see cref="Change"/>, present only where the reader
/// may change the Tangent, takes <c>name</c>, <c>description</c>, <c>accent</c>, <c>image</c>, <c>kind</c>, <c>audience</c> and <c>move</c>
/// (<c>earlier</c> or <c>later</c>), each left as it is when not sent. <see cref="Fixed"/> says this is the moderators'
/// Tangent, whose audience and place stay as they are. <see cref="Access"/> is where the roles' access to it is set
/// (<see cref="TangentAccessPage"/>). <see cref="Can"/> is what the reader holds on the Tangent, and
/// <see cref="Gate"/> why they may not change it, null when they may; <see cref="SignIn"/> is where a visitor signs in,
/// coming back here. After a refusal the form keeps what was sent and says why: under the name, under the description,
/// or above the form. <see cref="Delete"/>, present only where the reader holds <c>ManageSpace</c> and this is not the
/// moderators' Tangent, deletes it with the <see cref="Topics"/> it holds, which go with it; where it holds any, the page
/// says how many copies of them are out on Bluesky (<see cref="OnBluesky"/>, a lower bound where
/// <see cref="OnBlueskyAtLeast"/>), and where the Tangent downloads first (<see cref="Export"/>); after a refusal of the
/// delete it says why (<see cref="DeleteFailure"/>).</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record TangentSettingsPage(
    string Slug,
    string Name,
    string Description,
    string Accent,
    string Tone,
    string Image,
    IReadOnlyList<Space.AppearanceSettingsPage.Cover> Covers,
    string Kind,
    string Audience,
    string? Before,
    string? After,
    bool Fixed,
    bool Movable,
    string Address,
    string Access,
    Form? Change,
    IReadOnlyList<string> Can,
    string? Gate,
    IReadOnlyList<string> Kinds,
    IReadOnlyList<string> Audiences,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null,
    string? NameFailure = null,
    string? DescriptionFailure = null)
{
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new("Tangent settings", "Settings of " + Render.Sentence.Link(Name, Address));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(Gate, null, SignIn);

    /// <summary>The letter a tile shows of the Tangent.</summary>
    private string Initial => Name.Length > 0 ? char.ToUpperInvariant(Name[0]).ToString() : "T";

    /// <summary>The form as the page draws it: the reader's to send, or read only where it is not, with what it holds and why
    /// a change was refused; its moves are a form of their own below it.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form Shown
    {
        get
        {
            var form = Change ?? (TangentWrites.ChangeForm("", Fixed, false) with { ReadOnly = true });
            List<Render.Form.Choice> images = [new("", "None")];
            if (Image.StartsWith("/media/", StringComparison.Ordinal)) images.Add(new(Image, "Its own") { Picture = Door + Image.TrimStart('/') });
            images.AddRange(Covers.Select(cover => new Render.Form.Choice(cover.Name, cover.Title) { Picture = cover.Address }));
            var settings = Access[..Access.LastIndexOf('/')];
            return form with
            {
                Fields = Placed(form.Fields).Where(item => item.Name != TangentWrites.MoveField).Select(item => item.Name switch
                {
                    TangentWrites.NameField => item with { Value = Name, Failure = NameFailure, Note = "Its address stays `/t/" + Slug + "`, whatever its name." },
                    TangentWrites.DescriptionField => item with { Value = Description, Failure = DescriptionFailure },
                    TangentWrites.KindField => item with { Value = Kind },
                    TangentWrites.AudienceField when !Fixed => item with { Value = Audience },
                    TangentWrites.AccentField => item with
                    {
                        Value = Accent,
                        Choices = [new("", "From its name") { Tile = (Tone, Initial) }, .. Render.Tile.Tones.Select(tone =>
                            new Render.Form.Choice(tone, char.ToUpperInvariant(tone[0]) + tone[1..]) { Tile = (tone, Initial) })]
                    },
                    TangentWrites.ImageField => item with { Value = Image, Choices = images },
                    _ => item
                }).ToList(),
                Failure = Failure,
                Key = "settings",
                Cancel = new Render.Onward("Back to the Tangent", Address),
                Note = Gate is null && !Fixed
                    ? Render.Sentence.Link("Access", Access) + " sets which roles read and post here; "
                      + Render.Sentence.Link("People", settings + "/" + TangentPeopleController.Segment) + ", who joins and who is in."
                    : null
            };
        }
    }

    /// <summary>The form's fields with the audience said in a sentence in its place where it stays as it is, and, for a
    /// reader who may change the Tangent, the way to bring in an image of their own under the image's choices.</summary>
    private IEnumerable<Render.Form.Field> Placed(IReadOnlyList<Render.Form.Field> fields)
    {
        foreach (var item in fields)
        {
            yield return item;
            if (item.Name == TangentWrites.ImageField && Change is { } change)
                yield return new(Posts.NewMediaController.FileField, "", As: Render.Form.Input.File)
                {
                    Label = "Or bring your own", Accept = "image/png,image/jpeg,image/webp",
                    Note = "A PNG, JPEG or WebP, square. Its tile wears it at once in place of its letter, wherever the Tangent is listed.",
                    Sends = Render.Form.Posting(change.Action + "/" + TangentWrites.ImageSegment)
                        with { Tool = "change-tangent", Key = "tangent-image", Submit = "Use this image", Busy = "Bringing it in…", Multipart = true }
                };
            if (Fixed && item.Name == TangentWrites.KindField)
                yield return new(TangentWrites.AudienceField, "", "Only moderators read this Tangent; its audience stays as it is.", Render.Form.Input.Fixed) { Label = "Audience" };
        }
    }

    /// <summary>Where the Tangent stands among the Tangents, and, for a reader who may move it, one move earlier or later, the
    /// one at its end unpressable.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Moves => !Movable ? null : new Render.Form(Change?.Action ?? "", HttpMethods.Post,
        [
            new(TangentWrites.MoveField, Holds.TangentMove, As: Render.Form.Input.Switch)
            {
                Label = "Place among the Tangents",
                Note = (Fixed, Before, After) switch
                {
                    (true, _, _) => "It comes after every other Tangent, and stays there.",
                    (_, { } before, { } after) => $"It comes after {Render.Sentence.Plain(before)} and before {Render.Sentence.Plain(after)}.",
                    (_, null, { } after) => $"It comes first, before {Render.Sentence.Plain(after)}.",
                    (_, { } before, null) => $"It comes last, after {Render.Sentence.Plain(before)}.",
                    _ => "It is the only Tangent you can read."
                },
                Choices = Fixed
                    ? []
                    : [new(TangentWrites.Word(Move.Earlier), "Move earlier") { Locked = Before is null }, new(TangentWrites.Word(Move.Later), "Move later") { Locked = After is null }]
            }
        ]) { Tool = "change-tangent move-tangent", ReadOnly = Gate is not null || Change is null };

    public Render.Form? Delete { get; init; }

    public int Topics { get; init; }

    public int? OnBluesky { get; init; }

    public bool OnBlueskyAtLeast { get; init; }

    public string? Export { get; init; }

    public string? DeleteFailure { get; init; }

    /// <summary>Deleting the Tangent, at the foot of the page: an empty one at one press, one that holds Topics behind what
    /// goes with it, said in the page, opened again with the reason where a delete was refused.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Deleting => Delete is not { } delete
        ? null
        : Topics == 0
            ? delete with { Submit = "Delete this Tangent", Quiet = true, Failure = DeleteFailure }
            : delete with { Opens = "Delete this Tangent", Open = DeleteFailure is not null, Failure = DeleteFailure, Confirm = Warning };

    /// <summary>What deleting a Tangent that holds Topics takes along, in words: its Topics and their replies, for good; the
    /// copies of them out on Bluesky, which come down wherever their authors' accounts still let the place; and where a
    /// copy of it downloads first.</summary>
    private string Warning
    {
        get
        {
            var (one, many, reply) = Kind == Posts.PostWrites.Word(Posts.PostKind.Article) ? ("article", "articles", "to") : ("topic", "topics", "in");
            var goes = Topics == 1
                ? $"Its one {one} and every reply {reply} it go for good, and no tombstone stays in its place."
                : $"Its {Topics.ToString(System.Globalization.CultureInfo.InvariantCulture)} {many} and every reply {reply} them go for good, and no tombstone stays in their place.";
            var outside = OnBluesky switch
            {
                null or 0 => null,
                1 when Topics == 1 => "It is on Bluesky too, and is taken down there if its author’s account still lets this place.",
                1 => "One of them is on Bluesky too, and is taken down there if its author’s account still lets this place.",
                { } count => (OnBlueskyAtLeast ? "At least " : "") + count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                             + " of them are on Bluesky too, and are taken down there wherever their authors’ accounts still let this place."
            };
            var kept = Export is { } export ? Render.Sentence.Link("Download it first", export) + " to keep a copy." : null;
            return string.Join(' ', new[] { goes, outside, kept }.OfType<string>());
        }
    }

    /// <summary>The frame the shell draws around the page: its settings as its own crumb past the Tangent, and who reads, or
    /// where a visitor signs in.</summary>
    public static Render.Frame Frame(TangentSettingsPage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, page.SignIn);
}
