using System.Diagnostics;
using System.Net;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Connections;
using Tangent.Events;
using Tangent.Posts;
using Tangent.Space;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Participants;

/// <summary>The one-off move, scripts/once/a-person-is-the-places-own.ps1, on a place stored as it was, with each
/// participant's DID as their id: it gives each a GUID v7, binds them to the DID, and rewrites every row that names them,
/// so the place opens, its people sign in as themselves with their Posts, roles and marks; a second run moves nothing.
/// Deleted with the script once it has run.</summary>
public sealed class APersonIsThePlacesOwnTests
{
    private const string Leo = "did:plc:leo4move";
    private const string Bea = "did:plc:bea4move";

    [Fact]
    public async Task The_script_moves_everyone_and_every_row_that_names_them_and_a_second_run_moves_nothing()
    {
        await using var stored = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        var data = stored.DataFolder;
        Post root;
        {
            // The place as it was stored: established by Leo, whose id was his DID, Bea a member who wrote a reply that
            // mentions him by at:// and his DID, his mark of what he has seen, his own overwrite, and a receipt.
            await Persona.Bind(stored, Leo, Cancellation);
            await Persona.Bind(stored, Bea, Cancellation);
            Assert.IsType<Outcome<TangentSpace>.Done>(await new Establish(Leo, Leo, "leo.example", "Field notes").Send(Cancellation));
            Assert.IsType<Outcome<Participant>.Done>(await new FirstArrival(Bea, Bea, "bea.example").Send(Cancellation));
            var roles = stored.Services.GetRequiredService<RoleCollection>();
            await roles.Overwrite(Tangents.Overwrites.Resource(FirstTangentSlug), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.Post)], [], ct: Cancellation);
            root = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), FirstTangentSlug, "Lanterns", "Who lights theirs first?", Leo).Send(Cancellation)).Result;
            var reply = Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), root.Id, PostBody.Of("Whoever is first home."), Bea).Send(Cancellation)).Result;
            reply.Body = "Ask [@leo](at://" + Leo + ").";
            reply.MentionedIds = [Leo];
            reply.AuthorDid = "";
            Assert.IsType<Outcome<Post>.Done>(await new Store<Post>(reply).Send(Cancellation));
            Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(Leo, Scope.Topic(root.Id), 1)).Send(Cancellation));
            Assert.IsType<Outcome<Receipt>.Done>(await new Store<Receipt>(new Receipt
            {
                Id = Receipt.Key(Leo, root.Id), ConnectionId = Leo, PostId = root.Id, TangentId = root.TangentId, Version = 1,
                Document = "at://" + Leo + "/" + AtprotoAdapter.Documents + "/" + root.Id, At = DateTimeOffset.UtcNow
            }).Send(Cancellation));
            await stored.StopAsync(Cancellation);
        }

        var first = await Move(data);
        Assert.Contains("Moved", first);
        Assert.Contains("2 participants have ids of the place's own", first);
        Assert.Matches(@"role role:administrator: 1", first);
        Assert.Matches(@"posts, mentions in the body: 1", first);
        Assert.Single(Directory.GetFiles(data, "tangent.db.before-a-person-is-the-places-own-*"));

        await using var moved = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>(Settings()) { ["Tangent:DataFolder"] = data }, ct: Cancellation);
        var leo = await Persona.Arrived(Leo, Cancellation);
        var bea = await Persona.Arrived(Bea, Cancellation);
        Assert.NotNull(leo);
        Assert.NotNull(bea);
        Assert.True(Participant.IsId(leo.Id) && Participant.IsId(bea.Id));
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal((leo.Id, leo.Id), (space?.ProprietorId, space?.OperatorId));
        var rootNow = await Post.Get(root.Id, Cancellation);
        Assert.Equal((leo.Id, Leo, bea.Id), (rootNow?.AuthorId, rootNow?.AuthorDid, rootNow?.LatestReplyAuthorId));
        var replyNow = (await Post.Get(rootNow!.LatestReplyId!, Cancellation))!;
        Assert.Equal((bea.Id, Bea, "Ask [@leo](/p/" + leo.Id + ")."), (replyNow.AuthorId, replyNow.AuthorDid, replyNow.Body));
        Assert.Equal([leo.Id], replyNow.MentionedIds);
        Assert.NotNull(await Checkpoint.Get(Checkpoint.Key(leo.Id, Scope.Topic(root.Id)), Cancellation));
        Assert.Equal(leo.Id, (await Receipt.Get(Receipt.Key(leo.Id, root.Id), Cancellation))?.ConnectionId);
        Assert.All(await Event.All(Cancellation), e => Assert.False(e.ActorId == Leo || e.ActorId == Bea || e.TargetId == Leo || e.TargetId == Bea));
        var given = moved.Services.GetRequiredService<RoleCollection>();
        Assert.Contains(Roles.Administrator, (await given.ForPerson(leo.Id, Cancellation)).Select(role => role.Key));
        Assert.Equal(bea.Id, (await PermissionOverwrite.Get(PermissionOverwrite.KeyFor(Tangents.Overwrites.Resource(FirstTangentSlug), OverwriteSubjectKind.Person, bea.Id), Cancellation))?.SubjectId);

        // Leo signs in as his DID and lands on himself, his Topic his, and the mention of him reads his name.
        using var browser = new Browser(moved);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var door = await Send(browser.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(leo.Id, door.Json.GetProperty("who").GetProperty("id").GetString());
        var page = await Send(browser.Client, HttpMethod.Get, TopicPath(rootNow));
        Assert.Equal(HttpStatusCode.OK, page.Status);
        Assert.Contains("href=\"" + BaseUrl + "/p/leo.example\" title=\"@leo.example\">@leo.example</a>", page.Body);
        await moved.StopAsync(Cancellation);

        Assert.Contains("Nothing to move", await Move(data));
    }

    private const string FirstTangentSlug = "general";

    private static async Task<string> Move(string data)
    {
        var script = Path.Combine(Root(), "scripts", "once", "a-person-is-the-places-own.ps1");
        using var process = Process.Start(new ProcessStartInfo("pwsh", ["-NoProfile", "-File", script, "-DataFolder", data])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;
        var output = process.StandardOutput.ReadToEndAsync(Cancellation);
        var errors = process.StandardError.ReadToEndAsync(Cancellation);
        await process.WaitForExitAsync(Cancellation).WaitAsync(TimeSpan.FromMinutes(2), Cancellation);
        Assert.True(process.ExitCode == 0, await errors + await output);
        return await output;
    }

    private static string Root()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
            if (File.Exists(Path.Combine(folder.FullName, "Tangent.sln"))) return folder.FullName;
        throw new InvalidOperationException("No Tangent.sln above " + AppContext.BaseDirectory);
    }
}
