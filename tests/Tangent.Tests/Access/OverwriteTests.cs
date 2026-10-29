using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent.Access;

/// <summary>A Tangent's overwrites are Koan's rows, written through its RoleCollection and resolved by Koan in Discord's
/// order behind <see cref="Capabilities"/>: Discord's cases on those rows, the gate that names the deciding overwrite in
/// the words it has always had, and no overwrite type of Tangent's own.</summary>
public sealed class OverwriteTests
{
    private const string Reader = "did:plc:reader";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>A Tangent Space where Koan's test connector signs people in, and no profile job reaches the network.</summary>
    private static readonly Dictionary<string, string?> TestConnector = new()
    {
        ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
        ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
        ["Tangent:Profile:Timeout"] = "00:00:01"
    };

    [Fact]
    public async Task Discords_cases_resolve_through_capabilities_on_the_overwrites_koan_holds()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        foreach (var slug in new[] { "one", "two", "three", "four" }) await Given.ATangent(tangentSpace, slug, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "ada", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "bo", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "eve", [], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        const Bit everything = Bits.All & ~Bit.Administrator;

        // Everyone denied, then a role the participant holds allowed.
        await Write(roles, "one", OverwriteSubjectKind.Role, RoleTokens.Everyone, allow: Bit.View, deny: Bit.Topic);
        await Write(roles, "one", OverwriteSubjectKind.Role, Roles.Member, allow: Bit.Topic);
        // A role denied, then the participant's own allow.
        await Write(roles, "two", OverwriteSubjectKind.Role, Roles.Member, deny: Bit.Post);
        await Write(roles, "two", OverwriteSubjectKind.Person, "ada", allow: Bit.Post);
        // A role allowed, then the participant's own deny.
        await Write(roles, "three", OverwriteSubjectKind.Role, Roles.Member, allow: Bit.Moderate);
        await Write(roles, "three", OverwriteSubjectKind.Person, "ada", deny: Bit.Moderate);
        // Every deny there is, against an administrator.
        await Write(roles, "four", OverwriteSubjectKind.Role, RoleTokens.Everyone, deny: everything);
        await Write(roles, "four", OverwriteSubjectKind.Role, Roles.Administrator, deny: everything);
        await Write(roles, "four", OverwriteSubjectKind.Person, "leo", deny: everything);

        Assert.True((await Capabilities.Load("ada", "one", null, Cancellation)).Holds(Bit.Topic));
        Assert.Equal("everyone denies Topic", (await Capabilities.Load("eve", "one", null, Cancellation)).Gate(Bit.Topic));

        Assert.True((await Capabilities.Load("ada", "two", null, Cancellation)).Holds(Bit.Post));
        Assert.Equal("role:member denies Post", (await Capabilities.Load("bo", "two", null, Cancellation)).Gate(Bit.Post));

        Assert.Equal("ada denies Moderate", (await Capabilities.Load("ada", "three", null, Cancellation)).Gate(Bit.Moderate));
        Assert.True((await Capabilities.Load("bo", "three", null, Cancellation)).Holds(Bit.Moderate));

        var own = Post.Root(Tids.Mint(), "four", "Mine", "mine", PostKind.Topic, "Words.", "leo", DateTimeOffset.UtcNow);
        var administrator = await Capabilities.Load("leo", "four", own, Cancellation);
        Assert.Equal(Bits.All, administrator.Held);
        Assert.Equal(Bits.Names(Bits.All), administrator.Can);
        var member = await Capabilities.Load("ada", "four", null, Cancellation);
        Assert.Equal((false, Capabilities.Absent), (member.Exists, member.Gate(Bit.View)));
    }

    [Fact]
    public async Task The_gate_for_a_denied_verb_names_the_deciding_overwrite_to_a_command_and_on_the_page()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.ATangent(tangentSpace, "workshop", ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, "bo", [Roles.Member], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        await Write(roles, "workshop", OverwriteSubjectKind.Role, Roles.Member, deny: Bit.Topic);
        await Write(roles, "workshop", OverwriteSubjectKind.Person, Reader, deny: Bit.Post);
        var topic = Assert.IsType<Outcome<Post>.Done>(await writer.Send(
            new StartTopic(Tids.Mint(), "workshop", "Open to replies", "Words.", "leo"), Cancellation)).Result;

        var started = await writer.Send(new StartTopic(Tids.Mint(), "workshop", "Not here", "Words.", "bo"), Cancellation);
        var replied = await writer.Send(new Reply(Tids.Mint(), topic.Id, "Not me.", Reader), Cancellation);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new Reply(Tids.Mint(), topic.Id, "Me, yes.", "bo"), Cancellation));

        Assert.Equal("role:member denies Topic", Assert.IsType<Outcome<Post>.Refused>(started).Reason);
        Assert.Equal("did:plc:reader denies Post", Assert.IsType<Outcome<Post>.Refused>(replied).Reason);

        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/t/workshop/open-to-replies");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await browser.Client.SendAsync(request, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var page = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("did:plc:reader denies Post", page.RootElement.GetProperty("gate").GetString());
        Assert.DoesNotContain("Post", page.RootElement.GetProperty("can").EnumerateArray().Select(verb => verb.GetString()));
    }

    [Fact]
    public void No_type_in_the_tree_is_an_overwrite_of_tangents_own()
    {
        var types = new[] { typeof(Capabilities).Assembly, typeof(OverwriteTests).Assembly }.SelectMany(assembly => assembly.GetTypes()).ToList();

        Assert.Null(typeof(Capabilities).Assembly.GetType("Tangent.Tangents.Overwrite"));
        Assert.DoesNotContain(types, type => typeof(IEntity).IsAssignableFrom(type) && type.Name.Contains("Overwrite", StringComparison.Ordinal));
    }

    private static Task<PermissionOverwrite> Write(RoleCollection roles, string tangentId, OverwriteSubjectKind kind, string subjectId,
        Bit allow = Bit.None, Bit deny = Bit.None)
        => roles.Overwrite(Overwrites.Resource(tangentId), kind, subjectId, Bits.Tokens(allow), Bits.Tokens(deny), ct: Cancellation);
}
