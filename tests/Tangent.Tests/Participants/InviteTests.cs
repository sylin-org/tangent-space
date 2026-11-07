using System.Net;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>Inviting by handle: on a place whose admission says apply, whoever holds ManageStanding names a Bluesky
/// account by its handle on the applications, which lists it with the name its profile shows; its holder arrives admitted,
/// a member, never among the applications, and the invitation is used up. A withdrawn invitation leaves its holder to
/// apply; naming someone who applied admits them at once, and someone who arrived already is refused. A handle no account
/// answers to, or one the profile service could not verify, is refused on its field; where admission says join there is
/// no invitation; and anyone without ManageStanding is refused.</summary>
public sealed class InviteTests
{
    private const string Path = ApplicationsController.Path;
    private const string Grace = "did:plc:grace";
    private const string Hal = "did:plc:hal";
    private const string Ivy = "did:plc:ivy";

    private static async Task<bool> IsMember(TangentSpaceFixture tangentSpace, string id)
        => (await tangentSpace.Services.GetRequiredService<RoleCollection>().Bag(id, authenticated: true, Cancellation)).InRole(Roles.Member);

    private static async Task<Participant> Arrives(TangentSpaceFixture tangentSpace, string did)
    {
        using var browser = new Browser(tangentSpace);
        var name = did.Split(':')[^1];
        (await browser.SignIn(name, did, Cancellation, handle: name + ".example")).Dispose();
        return (await Persona.Arrived(did, Cancellation))!;
    }

    /// <summary>The settings of a place whose profiles the stub answers.</summary>
    private static Dictionary<string, string?> Answered(ProfileStub stub)
    {
        var settings = Settings();
        settings["Tangent:Profile:BaseAddress"] = stub.Answering.AbsoluteUri;
        return settings;
    }

    /// <summary>A place whose admission says apply, with a moderator, who holds ManageStanding, and a member.</summary>
    private static async Task Apply(TangentSpaceFixture tangentSpace)
    {
        await Given.AnEstablishedTangentSpace(tangentSpace, "Field notes", admission: Admission.Apply, ct: Cancellation);
        await Given.ATangent(tangentSpace, Workshop, ct: Cancellation);
        await Given.AParticipant(tangentSpace, Mo, [Roles.Moderator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Ada, [Roles.Member], ct: Cancellation);
    }

    [Fact]
    public async Task A_manager_invites_a_handle_whose_holder_arrives_a_member_and_a_withdrawn_one_applies_and_an_applicant_named_is_admitted_now()
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Answered(stub), ct: Cancellation);
        await Apply(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);

        var empty = (await Send(mo.Client, HttpMethod.Get, Path, json: true)).Json;
        Assert.Equal([ApplicationsController.HandleField], FieldNames(empty.GetProperty("invite")));
        Assert.Equal(0, empty.GetProperty("invitations").GetArrayLength());
        Assert.Contains("Invite by handle", (await Send(mo.Client, HttpMethod.Get, Path)).Body);

        var invited = await Form(mo, Path, ApplicationsController.InvitePath, new Dictionary<string, string> { [ApplicationsController.HandleField] = " @Grace.example" });

        Assert.Equal((HttpStatusCode.SeeOther, BaseUrl + Path), (invited.Status, invited.Location));
        Assert.Contains("grace.example", stub.Requests);
        var listed = (await Send(mo.Client, HttpMethod.Get, Path, json: true)).Json.GetProperty("invitations");
        Assert.Equal((Grace, "grace.example", ProfileStub.DisplayName), (listed[0].GetProperty("account").GetString(), listed[0].GetProperty("handle").GetString(),
            listed[0].GetProperty("name").GetString()));
        var page = PageContent((await Send(mo.Client, HttpMethod.Get, Path)).Body);
        Assert.Contains(ProfileStub.DisplayName, page);
        Assert.Contains("@grace.example", page);
        Assert.Contains(" invited ", page);
        // Inviting again changes nothing.
        Assert.Equal((HttpStatusCode.OK, "invited"), await State(mo, "grace.example"));
        Assert.Equal(1, (await Send(mo.Client, HttpMethod.Get, Path, json: true)).Json.GetProperty("invitations").GetArrayLength());

        var grace = await Arrives(tangentSpace, Grace);
        Assert.Equal(Standing.Admitted, grace.Standing);
        Assert.Null(grace.AppliedAt);
        Assert.True(await IsMember(tangentSpace, grace.Id));
        Assert.Null(await Invitation.Get(Grace, Cancellation));
        var after = (await Send(mo.Client, HttpMethod.Get, Path, json: true)).Json;
        Assert.Equal(0, after.GetProperty("applications").GetArrayLength());
        Assert.Equal(0, after.GetProperty("invitations").GetArrayLength());
        // Someone who arrived already is no one to invite.
        var here = await Json(mo, Path, ApplicationsController.InvitePath, new { handle = "grace.example" });
        Assert.Equal((HttpStatusCode.Conflict, "here"), (here.Status, here.Word));

        // Withdrawn, the invitation leaves its holder to apply as anyone does.
        Assert.Equal((HttpStatusCode.OK, "invited"), await State(mo, "hal.example"));
        var withdrawn = await Json(mo, Path, ApplicationsController.WithdrawPath, new { account = Hal });
        Assert.Equal((HttpStatusCode.OK, Hal), (withdrawn.Status, withdrawn.Json.GetProperty("account").GetString()));
        Assert.Equal(Standing.Applied, (await Arrives(tangentSpace, Hal)).Standing);

        // Naming someone who applied admits them at once.
        var ivy = await Arrives(tangentSpace, Ivy);
        Assert.Equal(Standing.Applied, ivy.Standing);
        Assert.Equal((HttpStatusCode.OK, "admitted"), await State(mo, "ivy.example"));
        Assert.Equal(Standing.Admitted, (await Participant.Get(ivy.Id, Cancellation))!.Standing);
        Assert.True(await IsMember(tangentSpace, ivy.Id));
        Assert.Null(await Invitation.Get(Ivy, Cancellation));
    }

    [Fact]
    public async Task A_handle_no_account_answers_to_is_refused_on_its_field_and_join_places_and_anyone_without_manage_standing_invite_no_one()
    {
        await using var stub = await ProfileStub.StartAsync(Cancellation);
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Answered(stub), ct: Cancellation);
        await Apply(tangentSpace);
        using var mo = await SignedIn(tangentSpace, Mo);
        using var ada = await SignedIn(tangentSpace, Ada);

        foreach (var handle in new[] { "nobody.example", "unverified.example", "not a handle" })
        {
            var refused = await Json(mo, Path, ApplicationsController.InvitePath, new { handle });
            Assert.Equal((HttpStatusCode.BadRequest, "invalid"), (refused.Status, refused.Word));
            Assert.Single(refused.Json.GetProperty("errors").GetProperty(ApplicationsController.HandleField).EnumerateArray());
        }
        Assert.DoesNotContain("not a handle", stub.Requests);
        var asForm = await Form(mo, Path, ApplicationsController.InvitePath, new Dictionary<string, string> { [ApplicationsController.HandleField] = "nobody.example" });
        Assert.Equal(HttpStatusCode.BadRequest, asForm.Status);
        Assert.Contains("No Bluesky account answers to @nobody.example", asForm.Body);
        Assert.Contains("value=\"nobody.example\"", asForm.Body);
        Assert.Empty(await Invitation.All(Cancellation));

        var gated = await Json(ada, "/", ApplicationsController.InvitePath, new { handle = "grace.example" });
        Assert.Equal(HttpStatusCode.Forbidden, gated.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Json(ada, "/", ApplicationsController.WithdrawPath, new { account = Grace })).Status);

        var space = (await TangentSpace.Get(TangentSpace.SingleRow, Cancellation))!;
        space.Admission = Admission.Join;
        Assert.IsType<Outcome<TangentSpace>.Done>(await new Store<TangentSpace>(space).Send(Cancellation));
        Assert.False((await Send(mo.Client, HttpMethod.Get, Path, json: true)).Json.TryGetProperty("invite", out _));
        Assert.DoesNotContain("Invite by handle", (await Send(mo.Client, HttpMethod.Get, Path)).Body);
        var joins = await Json(mo, Path, ApplicationsController.InvitePath, new { handle = "grace.example" });
        Assert.Equal((HttpStatusCode.Conflict, "joins"), (joins.Status, joins.Word));
        Assert.DoesNotContain("grace.example", stub.Requests);
    }

    private static async Task<(HttpStatusCode, string?)> State(Browser browser, string handle)
    {
        var answered = await Json(browser, Path, ApplicationsController.InvitePath, new { handle });
        return (answered.Status, answered.Status == HttpStatusCode.OK ? answered.Json.GetProperty("state").GetString() : answered.Word);
    }
}
