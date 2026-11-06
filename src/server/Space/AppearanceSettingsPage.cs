using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's appearance, one resource both ways: the <see cref="Sections"/> of the settings, appearance marked
/// current; its <see cref="Base"/>, one of <see cref="Bases"/>; its colours, its <see cref="Palette"/>, one of
/// <see cref="Palettes"/>; its own <see cref="Css"/>; its <see cref="Image"/>, empty, a cover's name or <c>/media/{id}</c>, at
/// <see cref="ImageAddress"/>, with the <see cref="Covers"/> Tangent bundles; its <see cref="Mark"/>, empty for its letter or
/// <c>/media/{id}</c>, at <see cref="MarkAddress"/>; and its words in Markdown: its
/// <see cref="Description"/>, its <see cref="Intro"/> under the door's head, the <see cref="Masthead"/> and <see cref="Footer"/>
/// lines and the <see cref="ArticleHeader"/>. <see cref="Change"/>, present only where the reader may change them, takes each
/// of them; <see cref="Preview"/>, beside it, takes the same and answers the door drawn in them, saving nothing; and
/// <see cref="Upload"/> and <see cref="MarkUpload"/> take an image of the reader's own as the place's image or its mark, or
/// the banner and the avatar their Bluesky account shows where <see cref="Bluesky"/> says it shows one. <see cref="Can"/> is what the reader holds in the
/// place, and <see cref="Gate"/> why they may not change them, null when they may; <see cref="SignIn"/> is where a visitor
/// signs in, coming back here. After a refusal the form says why above the choices.</summary>
[Render.Page(Render.Layout.Settings)]
public sealed record AppearanceSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string Base,
    IReadOnlyList<string> Bases,
    string Palette,
    IReadOnlyList<string> Palettes,
    string Css,
    string Image,
    string? ImageAddress,
    IReadOnlyList<AppearanceSettingsPage.Cover> Covers,
    string Mark,
    string? MarkAddress,
    string Description,
    string Intro,
    string Masthead,
    string Footer,
    string ArticleHeader,
    Form? Change,
    string? Preview,
    string? Upload,
    string? MarkUpload,
    AppearanceSettingsPage.Pictures Bluesky,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who,
    string? Failure = null)
{
    [Render.Slot(Render.Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public Render.SettingsRail Rail => new(Sections);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => new(TangentSpaceSettingsController.AppearanceSection, "Settings of " + Render.Sentence.Link(Place, Door));

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Notice? Line => Render.Notice.Of(Gate, null, SignIn);

    /// <summary>Whether the reader's Bluesky account showed a banner and an avatar when the place last read their profile.</summary>
    public sealed record Pictures(bool Banner, bool Avatar);

    /// <summary>The form as the page draws it: its preview, the place's image and its mark with the ways to bring one's own, its words, its
    /// colours and its base, each away from its default with the one press that returns it there, and, behind one quiet line,
    /// its lines, its article header and its own CSS; read only where the reader may not change them.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form Shown
    {
        get
        {
            var form = Change ?? (TangentSpaceSettingsController.AppearanceForm(Door.TrimEnd('/')) with { ReadOnly = true });
            Render.Form Resetting(string name, string value, string words) => Render.Form.Posting(form.Action, new Render.Form.Field(name, "", value, Render.Form.Input.Hidden))
                with { Tool = "change-appearance", Submit = words };
            List<Render.Form.Choice> images = [new("", "None")];
            if (Image.StartsWith("/media/", StringComparison.Ordinal) && ImageAddress is { } own) images.Add(new(Image, "Your own") { Picture = own });
            images.AddRange(Covers.Select(cover => new Render.Form.Choice(cover.Name, cover.Title) { Picture = cover.Address }));
            var fields = new List<Render.Form.Field>();
            foreach (var item in form.Fields)
            {
                fields.Add(item.Name switch
                {
                    TangentSpaceSettingsController.ImageField => item with
                    {
                        Value = Image, Choices = images,
                        Learned = Upload is { } bring && Bluesky.Banner ? FromBluesky(bring, "Use your Bluesky banner") : null
                    },
                    TangentSpaceSettingsController.MarkField => item with
                    {
                        Value = Mark,
                        Choices = Mark.Length > 0 && MarkAddress is { } mark
                            ? [Letter, new(Mark, "Your own") { Picture = mark }]
                            : [Letter],
                        Learned = MarkUpload is { } bring && Bluesky.Avatar ? FromBluesky(bring, "Use your Bluesky picture") : null
                    },
                    TangentSpaceSettingsController.DescriptionField => item with { Value = Description },
                    TangentSpaceSettingsController.IntroField => item with { Value = Intro },
                    TangentSpaceSettingsController.PaletteField => item with
                    {
                        Value = Palette,
                        Choices = Palettes.Select(word => new Render.Form.Choice(word, PaletteName(word), PaletteDetail(word)) { Swatch = Swatch(word) }).ToList(),
                        Reset = Palette != Appearance.Word(Space.Palette.Base)
                            ? Resetting(TangentSpaceSettingsController.PaletteField, Appearance.Word(Space.Palette.Base), "Return to the base’s own colours")
                            : null
                    },
                    TangentSpaceSettingsController.BaseField => item with
                    {
                        Value = Base,
                        Choices = Bases.Select(word => new Render.Form.Choice(word, BaseName(word), BaseDetail(word))).ToList(),
                        Reset = Base != Appearance.Word(Space.Base.Tangent)
                            ? Resetting(TangentSpaceSettingsController.BaseField, Appearance.Word(Space.Base.Tangent), "Return to Tangent’s own base")
                            : null
                    },
                    TangentSpaceSettingsController.MastheadField => item with { Value = Masthead },
                    TangentSpaceSettingsController.FooterField => item with { Value = Footer },
                    TangentSpaceSettingsController.ArticleHeaderField => item with { Value = ArticleHeader },
                    TangentSpaceSettingsController.CssField => item with
                    {
                        Value = Css,
                        Reset = Css.Length > 0
                            ? Resetting(TangentSpaceSettingsController.CssField, "", "Clear it") with
                            {
                                Opens = "Clear your CSS", Confirm = "Your CSS goes, and every page wears the other layers alone."
                            }
                            : null
                    },
                    _ => item
                });
                if (item.Name == TangentSpaceSettingsController.ImageField && Upload is { } upload)
                    fields.Add(Bringing("space-image", upload,
                        "A PNG, JPEG or WebP, wide. The place wears it at once behind the door’s head, and a link to the door shows it; anyone who reads the door sees it."));
                if (item.Name == TangentSpaceSettingsController.MarkField && MarkUpload is { } markUpload)
                    fields.Add(Bringing("space-mark", markUpload,
                        "A PNG, JPEG or WebP, square. The place wears it at once beside its name on every page and as every page’s icon."));
            }

            return form with { Fields = fields, Failure = Failure, Preview = Gate is null ? Preview : null };
        }
    }

    /// <summary>The place's letter, the mark it wears until a picture stands in for it.</summary>
    private Render.Form.Choice Letter => new("", "Its letter") { Tile = ("", Place.Length > 0 ? char.ToUpperInvariant(Place[0]).ToString() : "T") };

    /// <summary>The file a person brings in as the place's image or its mark, sent by its own button to <paramref name="action"/>.</summary>
    private static Render.Form.Field Bringing(string key, string action, string note)
        => new(TangentSpaceSettingsController.FileField, "", As: Render.Form.Input.File)
        {
            Label = "Or bring your own", Accept = "image/png,image/jpeg,image/webp", Note = note,
            Sends = Render.Form.Posting(action) with { Tool = "change-appearance", Key = key, Submit = "Use this image", Busy = "Bringing it in…", Multipart = true }
        };

    /// <summary>The one press that brings in the picture the reader's Bluesky account shows, sent to <paramref name="action"/>.</summary>
    public static Render.Form FromBluesky(string action, string words)
        => Render.Form.Posting(action, new Render.Form.Field(TangentSpaceSettingsController.FromField, Holds.From, TangentSpaceSettingsController.FromBluesky, Render.Form.Input.Hidden))
            with { Tool = "change-appearance", Submit = words, Busy = "Bringing it in…" };

    private static string Named(string word) => char.ToUpperInvariant(word[0]) + word[1..];

    private static string BaseName(string word) => word switch
    {
        "tangent" => "Tangent’s own",
        "simple" => "Simple.css",
        "mvp" => "MVP.css",
        "marx" => "Marx",
        "sakura" => "Sakura",
        "water" => "Water.css",
        "new" => "new.css",
        "concord" => "Concord",
        _ => Named(word)
    };

    private static string BaseDetail(string word) => word switch
    {
        "tangent" => "Newsreader and Schibsted Grotesk",
        "concord" => "A chat app’s look, Tangent’s own",
        _ => "Classless, in your system’s fonts"
    };

    /// <summary>The swatch a palette's card shows: the accent it wears, gold for the base's own under Tangent's own base, and
    /// none for another base's own colours.</summary>
    private string? Swatch(string word) => word != "base" ? word : Base == Appearance.Word(Space.Base.Tangent) ? "gold" : null;

    private string PaletteName(string word) => word == "base" ? "The base’s own" : Named(word);

    private string PaletteDetail(string word) => word != "base" ? "Tangent’s palette in " + word + ", over any base"
        : Base == Appearance.Word(Space.Base.Tangent) ? "Tangent’s palette in gold" : BaseName(Base) + "’s own colours";

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Render.Frame Frame(AppearanceSettingsPage page) => new(page.Place, page.Door, [new Render.Crumb("Settings")], page.Who, page.SignIn);


    /// <summary>A bundled cover a place may wear: its name, as <c>image</c> takes it, its title and its address.</summary>
    public sealed record Cover(string Name, string Title, string Address);
}
