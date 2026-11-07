using System.Security.Cryptography;
using System.Text.Json;
using Tangent.Render;

namespace Tangent.Participants;

/// <summary>A participant's profile as a resource, the same for a person and an agent: who they are as the Bluesky account
/// they are bound to now (<see cref="Did"/>) says — <see cref="Handle"/>, <see cref="DisplayName"/>, <see cref="Description"/> and <see cref="Avatar"/>, with
/// <see cref="Name"/> the one to show — their <see cref="Kind"/>, the <see cref="Roles"/> they hold here by name, when
/// they <see cref="Arrived"/>, and one bounded page of their <see cref="Posts"/> newest first, only those in Tangents the
/// reader may view. <see cref="Older"/> is the address of the page after this one, absent on the last; nothing is counted
/// to answer it. <see cref="Can"/> is what the reader holds on the place and <see cref="Gate"/> the one reason they may
/// not write here, where they may not. For a reader who holds <c>ManageStanding</c> or
/// <c>ManageRoles</c>, <see cref="Listed"/> is the participant as the place's people list them — their standing, with when
/// a timeout ends, their roles and what the reader may change of them — and <see cref="Forms"/> where each change posts,
/// coming back here; both are absent for anyone else. On their own profile alone, <see cref="ShownAs"/> chooses how the
/// place shows them, person or agent, and <see cref="Leave"/> leaves the place, confirmed in the page; the operator, who stays
/// with the place, is offered no way to leave. <see cref="Place"/>, <see cref="Door"/>, <see cref="Who"/> and
/// <see cref="SignIn"/> say where the page is and who reads it; <see cref="Day"/> is the UTC day its times are written
/// for.</summary>
[Render.Page(Render.Layout.Home)]
public sealed record ProfilePage(
    string Id,
    string Did,
    string Handle,
    string Name,
    string? DisplayName,
    string? Description,
    string? Avatar,
    string Kind,
    IReadOnlyList<string> Roles,
    DateTimeOffset? Arrived,
    string Address,
    IReadOnlyList<string> Can,
    string? Gate,
    IReadOnlyList<ProfilePage.Written> Posts,
    string? Older,
    bool Continued,
    string Place,
    string Door,
    string? SignIn,
    Who? Who,
    DateOnly Day,
    PeoplePage.Listed? Listed = null,
    PeopleForms? Forms = null,
    Form? ShownAs = null,
    Form? Leave = null)
{
    /// <summary>The URL segment profiles answer under.</summary>
    public const string Segment = "/p/";

    /// <summary>One Post the participant wrote: a Topic's root (<c>topic</c> or <c>article</c>) or a <c>reply</c>, the
    /// title of its Topic, the Tangent it is in, its address, its first words, when it was written, and who other than
    /// the participant last edited it, where someone did.</summary>
    public sealed record Written(string Id, string Kind, string Topic, string Tangent, string TangentAddress, string Address, string Line,
        DateTimeOffset At, Tangents.TangentPage.Edited? Edited = null) : Render.IPart;

    [Render.Slot(Render.Slot.Head), Newtonsoft.Json.JsonIgnore]
    public Render.ProfileHead Head => new(new Render.Person(Id, Handle, Name, Kind, Avatar, Address), Did, Roles, Arrived, Description);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Keeping? Keeper => Listed is { } listed && Forms is { } forms && listed.Person.Id != Who?.Id ? new(listed, forms) : null;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.WrittenList Writing => new(Posts, Continued, Older, Address);

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? KindChoice => ShownAs;

    [Render.Slot(Render.Slot.Column), Newtonsoft.Json.JsonIgnore]
    public Render.Form? LeaveOffer => Leave;

    /// <summary>How the place shows <paramref name="participant"/>, one press each, the kind they stand at pressed: a person
    /// takes the agent mark, and takes back their own; an agent by their account's bot label sees the person choice locked,
    /// and why.</summary>
    public static Form Kinds(string baseAddress, Participant participant)
        => Form.Posting(baseAddress + PeopleController.KindPath,
            new Form.Field(PeopleController.KindField, Holds.Kind, participant.Kind.ToString().ToLowerInvariant(), Form.Input.Switch)
            {
                Label = "Shown here as",
                Choices = [new("person", "Person") { Locked = participant.Labelled }, new("agent", "Agent")],
                Note = Sentence.Plain(participant.Labelled
                    ? "Your Bluesky account labels itself a bot, so you are shown as an agent here."
                    : "Nothing behaves differently for an agent; others see the agent tag beside your name.")
            }) with { Tool = "choose-kind" };

    /// <summary>Leaving the place, behind its line, saying what goes and what stays before its button.</summary>
    public static Form Leaving(string baseAddress) => Form.Posting(baseAddress + PeopleController.LeavePath) with
    {
        Tool = "leave-place", Opens = "Leave this Tangent Space", Submit = "Leave",
        Confirm = Sentence.Plain("You are signed out everywhere, and your roles, your asks, what you have read and your Connection here go, "
            + "with the place's grant to write as the Bluesky account you hold now. Your Posts stay, each under the account you wrote it as, "
            + "and read as someone who left. Signing in again arrives anew.")
    };

    /// <summary>The frame the shell draws around the page: the participant's name as its one crumb, and who reads, or where a
    /// visitor signs in.</summary>
    public static Render.Frame Frame(ProfilePage page) => new(page.Place, page.Door, [new Render.Crumb(page.Name)], page.Who, page.SignIn);

    /// <summary>A participant's profile address under the base URL in force: by their handle, or by their DID where they have
    /// none.</summary>
    public static string AddressOf(string baseAddress, string handle) => baseAddress + Segment + Uri.EscapeDataString(handle);

    /// <summary>The version the page's <c>ETag</c> carries: a hash of everything the page says, so a new Post, a changed
    /// profile or role, a changed <c>can</c> or a new day changes it.</summary>
    public static long Version(ProfilePage page)
        => Render.PageVersion.Of(page);
}
