using System.Diagnostics;
using System.Net;
using Koan.Data.Core;
using Koan.Data.Core.Commands;
using Koan.Identity.Roles;
using Koan.Jobs;
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
        Post reply, removed;
        string image;
        {
            // The place as it was stored, every row naming someone by the DID that was their id: established by Leo, with
            // a role whose word and a Tangent whose slug are not ASCII; Bea a member who wrote a reply that mentions him by
            // at:// and his DID, which Leo edited, so it has an earlier version, and another he removed; Bea's own
            // overwrite and ask in that Tangent; Leo's mark of what he has seen, his Connection, a receipt, his publication,
            // what came back for his article, an image he brought, and a job of his participant.
            await Persona.Bind(stored, Leo, Cancellation);
            await Persona.Bind(stored, Bea, Cancellation);
            Assert.IsType<Outcome<TangentSpace>.Done>(await new Establish(Leo, Leo, "leo.example", "Field notes").Send(Cancellation));
            Assert.IsType<Outcome<Participant>.Done>(await new FirstArrival(Bea, Bea, "bea.example").Send(Cancellation));
            var roles = stored.Services.GetRequiredService<RoleCollection>();
            await Given.ATangent(stored, Café, ct: Cancellation);
            await roles.Define(Gardener, "Gärtner", [], ct: Cancellation);
            await roles.Add(Gardener, Leo, Cancellation);
            await roles.Overwrite(Tangents.Overwrites.Resource(Café), OverwriteSubjectKind.Person, Bea, [Bits.Token(Bit.Post)], [], ct: Cancellation);
            Assert.IsType<Outcome<Tangents.Ask>.Done>(await new Store<Tangents.Ask>(new Tangents.Ask
            {
                Id = Tangents.Ask.Key(Café, Bea), TangentId = Café, PersonId = Bea, AskedAt = DateTimeOffset.UtcNow
            }).Send(Cancellation));

            root = Assert.IsType<Outcome<Post>.Done>(await new StartTopic(Tids.Mint(), FirstTangentSlug, "Lanterns", "Who lights theirs first?", Leo).Send(Cancellation)).Result;
            reply = Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), root.Id, PostBody.Of("Whoever is first home."), Bea).Send(Cancellation)).Result;
            Assert.IsType<Outcome<Post>.Done>(await new EditPost(reply.Id, 1, PostBody.Of("Whoever is first home, says Bea."), null, Leo).Send(Cancellation));
            var edited = (await Post.Get(reply.Id, Cancellation))!;
            edited.Body = "Ask [@leo](at://" + Leo + ").";
            edited.MentionedIds = [Leo];
            edited.AuthorDid = "";
            Assert.IsType<Outcome<Post>.Done>(await new Store<Post>(edited).Send(Cancellation));
            removed = Assert.IsType<Outcome<Post>.Done>(await new Reply(Tids.Mint(), root.Id, PostBody.Of("Off topic."), Bea).Send(Cancellation)).Result;
            Assert.IsType<Outcome<Post>.Done>(await new RemovePost(removed.Id, Leo).Send(Cancellation));

            Assert.IsType<Outcome<Checkpoint>.Done>(await new Store<Checkpoint>(Checkpoint.At(Leo, Scope.Topic(root.Id), 1)).Send(Cancellation));
            Assert.IsType<Outcome<Connection>.Done>(await new Store<Connection>(new Connection { Id = Leo, Since = DateTimeOffset.UtcNow }).Send(Cancellation));
            Assert.IsType<Outcome<Receipt>.Done>(await new Store<Receipt>(new Receipt
            {
                Id = Receipt.Key(Leo, root.Id), ConnectionId = Leo, PostId = root.Id, TangentId = root.TangentId, Version = 1,
                Document = "at://" + Leo + "/" + AtprotoAdapter.Documents + "/" + root.Id, At = DateTimeOffset.UtcNow
            }).Send(Cancellation));
            Assert.IsType<Outcome<Publication>.Done>(await new Store<Publication>(new Publication
            {
                Proprietor = Leo, Key = "3lpublication", Uri = "at://" + Leo + "/" + AtprotoAdapter.Publications + "/3lpublication", Cid = "bafyrei", Fingerprint = "-"
            }).Send(Cancellation));
            Assert.IsType<Outcome<Elsewhere>.Done>(await new Store<Elsewhere>(new Elsewhere { Id = root.Id, AuthorId = Leo }).Send(Cancellation));
            image = (await Media.Keep(ArticleMediaTests.Jpeg(), Cancellation)).Id;
            Assert.IsType<Outcome<MediaUse>.Done>(await new BringMedia(image, Leo).Send(Cancellation));
            var job = await (await Participant.Get(Leo, Cancellation))!.Job.Submit(ct: Cancellation);
            await job.Completion(TimeSpan.FromSeconds(20), Cancellation);
            await stored.StopAsync(Cancellation);
        }

        var first = await Move(data);
        Assert.Contains("Moved", first);
        Assert.Contains("2 participants have ids of the place's own", first);
        foreach (var step in new[] { "role role:administrator: 1", "role " + Gardener + ": 1", "overwrite on " + Tangents.Overwrites.Resource(Café) + ": 1",
                     "posts, mentions in the body: 1", "posts, EditedBy: 1", "posts, RemovedBy: 1", "earlier versions they wrote, keeping the account they wrote them as: 2",
                     "connection: 1", "receipts: 1", "what came back for their articles: 1", "publication: 1", "images they brought: 1", "asks to join: 1" })
            Assert.True(first.Contains(step, StringComparison.Ordinal), step + " is not in: " + first);
        Assert.Matches(@"audit events: [1-9]", first);
        Assert.Matches(@"jobs dropped: [1-9]", first);
        Assert.Empty(Directory.GetFiles(data, "*before-a-person*"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(data)!, Path.GetFileName(data) + ".before-a-person-is-the-places-own-*.db"));

        await using var moved = await TangentSpaceFixture.StartAsync(new Dictionary<string, string?>(Settings()) { ["Tangent:DataFolder"] = data }, ct: Cancellation);
        var leo = await Persona.Arrived(Leo, Cancellation);
        var bea = await Persona.Arrived(Bea, Cancellation);
        Assert.NotNull(leo);
        Assert.NotNull(bea);
        Assert.True(Participant.IsId(leo.Id) && Participant.IsId(bea.Id));
        var space = await TangentSpace.Get(TangentSpace.SingleRow, Cancellation);
        Assert.Equal((leo.Id, leo.Id), (space?.ProprietorId, space?.OperatorId));
        var rootNow = await Post.Get(root.Id, Cancellation);
        Assert.Equal((leo.Id, Leo), (rootNow?.AuthorId, rootNow?.AuthorDid));
        Assert.Equal([bea.Id, leo.Id], rootNow!.RecentAuthorIds);
        var replyNow = (await Post.Get(reply.Id, Cancellation))!;
        Assert.Equal((bea.Id, Bea, leo.Id, "Ask [@leo](/p/" + leo.Id + ")."), (replyNow.AuthorId, replyNow.AuthorDid, replyNow.EditedBy, replyNow.Body));
        Assert.Equal([leo.Id], replyNow.MentionedIds);
        var earlier = Assert.Single(await Data<Post, string>.All(AuditPartition.Name, Cancellation), version => version.Id.StartsWith(reply.Id, StringComparison.Ordinal));
        Assert.Equal((bea.Id, Bea), (earlier.AuthorId, earlier.AuthorDid));
        var removedNow = await Post.Get(removed.Id, Cancellation);
        Assert.Equal((leo.Id, bea.Id), (removedNow?.RemovedBy, rootNow.LatestReplyAuthorId));
        Assert.NotNull(await Checkpoint.Get(Checkpoint.Key(leo.Id, Scope.Topic(root.Id)), Cancellation));
        Assert.NotNull(await Connection.Get(leo.Id, Cancellation));
        Assert.Null(await Connection.Get(Leo, Cancellation));
        var receipt = await Receipt.Get(Receipt.Key(leo.Id, root.Id), Cancellation);
        Assert.Equal((leo.Id, Leo), (receipt?.ConnectionId, receipt?.Repository()));
        var publication = await Publication.Get(Publication.SingleRow, Cancellation);
        Assert.Equal((leo.Id, Leo), (publication?.Proprietor, publication?.Did));
        Assert.Equal(leo.Id, (await Elsewhere.Get(root.Id, Cancellation))?.AuthorId);
        Assert.NotNull(await MediaUse.Get(MediaUse.Key(image, leo.Id), Cancellation));
        Assert.Equal(bea.Id, (await Tangents.Ask.Get(Tangents.Ask.Key(Café, bea.Id), Cancellation))?.PersonId);
        Assert.All(await Event.All(Cancellation), e => Assert.False(e.ActorId == Leo || e.ActorId == Bea || e.TargetId == Leo || e.TargetId == Bea));
        var given = moved.Services.GetRequiredService<RoleCollection>();
        Assert.Contains(Roles.Administrator, (await given.ForPerson(leo.Id, Cancellation)).Select(role => role.Key));
        foreach (var role in new[] { Roles.Administrator, Gardener })
            Assert.Equal(leo.Id, (await RoleMember.Get(RoleMember.KeyFor(role, leo.Id), Cancellation))?.PersonId);
        Assert.Equal(bea.Id, (await PermissionOverwrite.Get(PermissionOverwrite.KeyFor(Tangents.Overwrites.Resource(Café), OverwriteSubjectKind.Person, bea.Id), Cancellation))?.SubjectId);

        // Leo signs in as his DID and lands on himself, and the mention of him reads his name.
        using var browser = new Browser(moved);
        (await browser.SignIn("leo", Leo, Cancellation, handle: "leo.example")).Dispose();
        var door = await Send(browser.Client, HttpMethod.Get, "/", json: true);
        Assert.Equal(leo.Id, door.Json.GetProperty("who").GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Send(browser.Client, HttpMethod.Get, TopicPath(rootNow))).Status);
        var page = await Send(Agent(moved), HttpMethod.Get, TopicPath(rootNow));
        Assert.Contains("href=\"" + BaseUrl + "/p/leo.example\" title=\"@leo.example\">@leo.example</a>", page.Body);
        await moved.StopAsync(Cancellation);

        Assert.Contains("Nothing to move", await Move(data));
    }

    private const string FirstTangentSlug = "general";
    private const string Café = "café";
    private const string Gardener = "role:gärtner";

    private static async Task<string> Move(string data)
    {
        var script = Path.Combine(Root(), "scripts", "once", "a-person-is-the-places-own.ps1");
        using var process = Process.Start(new ProcessStartInfo("pwsh", ["-NoProfile", "-File", script, "-DataFolder", data])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
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
