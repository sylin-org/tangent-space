using Tangent.Render;
using Tangent.Space;

namespace Tangent.Lifecycle;

/// <summary>Taking the place along, one resource both ways: the <see cref="Sections"/> of the settings, export marked
/// current; <see cref="Whole"/>, where the whole Tangent Space downloads, present only where the reader may export; and a
/// page of <see cref="Tangents"/> they may view, each with where it downloads, <see cref="Next"/> the address of the page
/// after, null on the last, and <see cref="First"/> the first page's, for a page that <see cref="Continued"/> from a cursor.
/// <see cref="Can"/> is what the reader holds in the place, and <see cref="Gate"/> why they may not export, null when they
/// may; <see cref="SignIn"/> is where a visitor signs in, coming back here.</summary>
[Page(Layout.Settings)]
public sealed record ExportSettingsPage(
    IReadOnlyList<SettingsSection> Sections,
    string? Whole,
    IReadOnlyList<ExportSettingsPage.Download> Tangents,
    string? Next,
    bool Continued,
    string First,
    IReadOnlyList<string> Can,
    string? Gate,
    string? SignIn,
    string Place,
    string Door,
    Who? Who)
{
    /// <summary>A Tangent the reader may take: its slug, name, kind, how many Topics were started there, and where its zip
    /// is.</summary>
    public sealed record Download(string Slug, string Name, string Kind, int TopicCount, string Address);

    [Slot(Slot.Rail), Newtonsoft.Json.JsonIgnore]
    public SettingsRail Rail => new(Sections);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public PanelTitle Heading => new(Space.TangentSpaceSettingsController.ExportSection, "Settings of " + Render.Sentence.Link(Place, Door));

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Notice? Line => Notice.Of(Gate, null, SignIn);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Says => Gate is null
        ? new("A zip of readable pages that open with no server, each beside its data as JSON. It holds everything you may read here, and removed posts stay as they stand.")
        : null;

    /// <summary>What the reader may download: the whole place, where the page is the first, then each Tangent.</summary>
    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public EntryList? Offered => Gate is not null ? null : new(
    [
        .. !Continued && Whole is { } whole ? new[] { new Entry("The whole of " + Render.Sentence.Plain(Place), [new LinkButton("Download", whole) { Main = true, Download = true }]) } : [],
        .. Tangents.Select(tangent => new Entry(Render.Sentence.Plain(tangent.Name), [new LinkButton("Download", tangent.Address) { Download = true }])
        {
            Detail = Render.Sentence.Plain(new TangentEntry(tangent.Slug, tangent.Name, tangent.Kind, tangent.Address, tangent.TopicCount).Detail)
        })
    ]);

    [Slot(Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Pager? Pages => Gate is not null ? null
        : new("More Tangents", Continued ? new Onward("First Tangents", First) : null, Next is { } next ? new Onward("More Tangents →", next) : null);

    /// <summary>The frame the shell draws around the page: its settings as its one crumb, and who reads, or where a visitor
    /// signs in.</summary>
    public static Frame Frame(ExportSettingsPage page) => new(page.Place, page.Door, [new Crumb("Settings")], page.Who, page.SignIn);
}
