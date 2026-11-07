using Microsoft.AspNetCore.WebUtilities;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>First run as a resource, in the one state the request finds it. Every state carries the place's
/// <see cref="Name"/> (<see cref="TangentSpace.Unnamed"/> until first run names it), the <see cref="Door"/>, and, where
/// the configuration names the account that may set the place up, that account as <see cref="Operator"/>: its handle
/// without the <c>@</c>, or its DID; with none named, whoever signs in first sets it up. For a visitor, and for
/// someone signed in who may set it up, it carries <see cref="Challenge"/>, the Koan connector's atproto challenge,
/// whose sign-in sets the place up and comes back here; signed in as anyone else, <see cref="Waiting"/>; once the place
/// is established, for its operator and its proprietor, the <see cref="BaseUrl"/> it answers at and the
/// <see cref="Pointers"/>, what the site places to point at it: none while the base URL is local, which no site can
/// point at, and <see cref="PointersLater"/> says instead when they appear. <see cref="Reader"/> is whoever is
/// signed in, as the account menu shows them, with where they sign out.</summary>
[Render.Page(Render.Layout.Panel)]
public sealed record FirstRun(
    string Name,
    string Door,
    string? Operator = null,
    string? Challenge = null,
    FirstRunWaiting? Waiting = null,
    string? BaseUrl = null,
    IReadOnlyList<Pointer>? Pointers = null,
    Render.Who? Reader = null,
    string? PointersLater = null)
{
    [Render.Slot(Render.Slot.Head), Newtonsoft.Json.JsonIgnore]
    public Render.PlaceBand Band => new(Name);

    /// <summary>Whether the configuration names the account that may set the place up by its DID.</summary>
    private bool NamedByDid => Operator?.StartsWith("did:", StringComparison.Ordinal) == true;

    /// <summary>Who alone may set the place up, in a sentence: their handle, with the DID the configuration names, or the DID.</summary>
    private string? Only => Waiting?.OperatorHandle is { } handle
        ? "Only " + Render.Sentence.Strong("@" + handle) + (NamedByDid ? " (" + Render.Sentence.Coded(Operator ?? "") + ")" : "") + " can set it up."
        : Operator is { } named ? "Only " + (NamedByDid ? Render.Sentence.Coded(named) : Render.Sentence.Strong("@" + named)) + " can set it up." : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.PanelTitle Heading => Pointers is not null
        ? new(Name + " is ready", Lede: "It answers at " + Render.Sentence.CodeLink(BaseUrl ?? "", Door)
            + (PointersLater is null ? "; place any one of these on your site." : ", which only this machine reaches."))
        : Waiting is { } waiting
            ? new("Waiting for its operator", Lede: (waiting.Handle.Length > 0 ? "You’re signed in as " + Render.Sentence.Strong("@" + waiting.Handle) : "You’re signed in")
                + ", and this place is still being set up: check back soon.")
            : new("Set up this Tangent Space", Lede: "Sign in with the account that will run it to set it up.");

    /// <summary>When the pointers will appear, while the place answers only at a local address.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Later => Pointers is not null && PointersLater is { } later ? new(later) : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Rule => Waiting is not null && Only is { } only ? new(only) : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Welcome => Pointers is not null ? new("Welcome: it is yours now. Its door shows the few steps that make it so.")
        : Waiting is not null ? new("If that is you, sign in with that account.") : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Pointing? Pointing => Pointers is { Count: > 0 } pointers ? new(pointers) : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Onward? Open => Pointers is not null ? new("Open " + Name, Door) : null;

    /// <summary>For someone waiting, the way to sign in as another account.</summary>
    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? Another => Pointers is null && Waiting is not null && Reader is { } reader
        ? Render.Form.Posting(reader.SignOut) with { Submit = "Use another account", Quiet = true, Tool = "first-run-switch" }
        : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public IReadOnlyList<Render.Form>? Ways => Pointers is null && Waiting is null
        ? Participants.SignIn.Forms(ChallengeAddress(), ChallengeReturn(), null, null, null, "first-run-sign-in")
        : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Sentence? Setter => Pointers is null && Waiting is null && Only is { } only ? new(only) : null;

    /// <summary>The frame the shell draws around the page: the step as its one crumb, the account of whoever is signed in, and
    /// no search.</summary>
    public static Render.Frame Frame(FirstRun firstRun)
        => new(firstRun.Name, firstRun.Door, [new Render.Crumb(firstRun.Pointers is null ? "First run" : "Pointers")],
            firstRun.Reader,
            Searchable: false);


    /// <summary>The configuration key that names the account that may set the place up.</summary>
    public const string OperatorSetting = "Tangent:Operator";

    /// <summary>Beside <see cref="Challenge"/>, how to sign in with an account on a server other than Bluesky's.</summary>
    public string? AnotherServer => Challenge is null ? null : Participants.SignIn.AnotherServerText;

    /// <summary>The challenge's address without its query: where the sign-in forms go.</summary>
    public string ChallengeAddress() => (Challenge ?? "").Split('?', 2)[0];

    /// <summary>The address the challenge comes back to, as the sign-in forms carry it.</summary>
    public string ChallengeReturn()
        => Challenge is not null && QueryHelpers.ParseQuery(new Uri(Challenge).Query).TryGetValue("return", out var value)
            ? value.ToString()
            : "";
}

/// <summary>A signed-in person who may not set the place up, since the configuration names another account: they
/// signed in as <see cref="Handle"/> (empty when the connector verified none), and <see cref="OperatorHandle"/> is the
/// named account's handle: the configured one, or, for a configured DID, the handle of the participant with that DID
/// where one has arrived here.</summary>
public sealed record FirstRunWaiting(string Handle, string? OperatorHandle = null);

