using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Access;
using Tangent.Events;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Writer;
using Xunit;

namespace Tangent.Tangents;

/// <summary>A Tangent set up by command: its row, its audience as the everyone overwrite, and the log row that says so;
/// and a private Tangent that exists for nobody its audience excludes at any instant of its creation.</summary>
public sealed class TangentTests(ITestOutputHelper output)
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
    public async Task A_tangent_created_by_command_exists_with_its_slug_as_key_and_appends_tangent_changed_carrying_its_audience()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        var announced = new List<Committed>();
        writer.Committed += announced.Add;
        var at = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

        var outcome = await writer.Send(new CreateTangent("the-workshop", "The workshop", "Where things get made", PostKind.Topic, 2, Audience.Private, "leo", at), Cancellation);

        var tangent = Assert.IsType<Outcome<Tangent>.Done>(outcome).Result;
        Assert.Equal("the-workshop", tangent.Id);
        var stored = await Tangent.Get("the-workshop", Cancellation);
        Assert.NotNull(stored);
        Assert.Equal(("The workshop", "Where things get made", PostKind.Topic, 2), (stored.Name, stored.Description, stored.DefaultKind, stored.Order));

        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        var everyone = Assert.Single(await roles.Overwrites(Overwrites.Resource("the-workshop"), Cancellation));
        Assert.Equal((OverwriteSubjectKind.Role, RoleTokens.Everyone), (everyone.SubjectKind, everyone.SubjectId));
        Assert.Equal(Audience.Private, await Overwrites.AudienceOf(roles, "the-workshop", Cancellation));
        Assert.Contains(Bits.Token(Bit.View), everyone.Deny);

        var logged = Assert.Single(await Event.All(Cancellation));
        Assert.Equal((1L, EventKind.TangentChanged, "the-workshop", "leo", Audience.Private, at), (logged.Seq, logged.Kind, logged.TangentId, logged.ActorId, logged.Audience, logged.At));
        Assert.Equal(Event.Key(1), logged.Id);
        var commit = Assert.Single(announced);
        Assert.Equal(1L, commit.Sequences[Event.Log]);
    }

    [Fact]
    public async Task The_same_slug_with_the_same_fields_answers_with_the_existing_tangent_and_different_fields_are_a_conflict()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);

        var first = Assert.IsType<Outcome<Tangent>.Done>(await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "leo"), Cancellation)).Result;
        var again = await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Public, "leo"), Cancellation);
        var differs = await writer.Send(new CreateTangent("articles", "Articles", "", PostKind.Article, 1, Audience.Private, "leo"), Cancellation);

        Assert.Equal(first.Id, Assert.IsType<Outcome<Tangent>.Done>(again).Result.Id);
        Assert.Contains("'articles'", Assert.IsType<Outcome<Tangent>.Conflict>(differs).Reason);
        Assert.Equal(1, await Event.Count.Exact(Cancellation));
        Assert.Equal(1, await Tangent.Count.Exact(Cancellation));
    }

    [Fact]
    public async Task A_slug_that_is_not_a_slug_is_refused_and_nothing_is_written()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);

        var refused = await writer.Send(new CreateTangent("The Workshop", "The workshop", "", PostKind.Topic, 1, Audience.Public, "leo"), Cancellation);

        Assert.Equal(Tangent.NeedsSlug, Assert.IsType<Outcome<Tangent>.Refused>(refused).Reason);
        Assert.Equal(0, await Tangent.Count.Exact(Cancellation));
        Assert.Equal(0, await Event.Count.Exact(Cancellation));
    }

    /// <summary>The order: the command writes the private audience's everyone deny before its transaction opens, so
    /// the deny holds while no row of the Tangent exists. A probe at that instant, and a reader asking all along, before,
    /// while and after the create, receive the absent answer for the Tangent's page and its Topic's page, and a door
    /// that does not name it, both as a visitor and as a member the audience excludes. A page reads the Tangent's row
    /// before its overwrites, so a reader who finds the row reads a deny that was written before it.</summary>
    [Fact]
    public async Task A_private_tangent_created_through_the_writer_is_absent_to_a_visitor_and_a_non_member_at_every_instant_of_its_creation()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(TestConnector, ct: Cancellation);
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var roles = tangentSpace.Services.GetRequiredService<RoleCollection>();
        await Given.AnEstablishedTangentSpace(tangentSpace, ct: Cancellation);
        await Given.AParticipant(tangentSpace, "leo", [Roles.Administrator], ct: Cancellation);
        await Given.AParticipant(tangentSpace, Reader, [Roles.Member], ct: Cancellation);
        using var browser = new Browser(tangentSpace);
        (await browser.SignIn("reader", Reader, Cancellation)).Dispose();

        var answers = new List<(string When, string Path, HttpStatusCode Status, string Body)>();
        async Task Read(string when)
        {
            foreach (var client in new[] { tangentSpace.Client, browser.Client })
            foreach (var path in new[] { "/t/studio", "/t/studio/behind-the-door", "/" })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, path);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await client.SendAsync(request, Cancellation);
                var body = await response.Content.ReadAsStringAsync(Cancellation);
                lock (answers) answers.Add((when, path, response.StatusCode, body));
            }
        }

        using var stop = new CancellationTokenSource();
        var asking = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested) await Read("all along");
        }, Cancellation);

        (Audience? Audience, Tangent? Row) between = default;
        var created = await writer.Send(new Between<Tangent>(
            new CreateTangent("studio", "The studio", "Behind the door.", PostKind.Topic, 2, Audience.Private, "leo"),
            async ct =>
            {
                between = (await Overwrites.AudienceOf(roles, "studio", ct), await Tangent.Get("studio", ct));
                await Read("between");
            }), Cancellation);
        Assert.IsType<Outcome<Post>.Done>(await writer.Send(new StartTopic(Tids.Mint(), "studio", "Behind the door", "Not for everyone.", "leo"), Cancellation));
        await Read("after");
        await stop.CancelAsync();
        await asking;

        var alongside = answers.Count(answer => answer.When == "all along");
        output.WriteLine($"Answers: {answers.Count}, of which {alongside} from the reader asking all along.");
        Assert.IsType<Outcome<Tangent>.Done>(created);
        Assert.Equal<(Audience?, Tangent?)>((Audience.Private, null), between);
        Assert.Equal(6, answers.Count(answer => answer.When == "between"));
        Assert.Equal(6, answers.Count(answer => answer.When == "after"));
        Assert.All(answers.Where(answer => answer.Path != "/"), answer =>
        {
            Assert.Equal(HttpStatusCode.NotFound, answer.Status);
            using var problem = JsonDocument.Parse(answer.Body);
            Assert.Equal(Capabilities.Absent, problem.RootElement.GetProperty("gate").GetString());
        });
        Assert.All(answers.Where(answer => answer.Path == "/"), answer =>
        {
            Assert.Equal(HttpStatusCode.OK, answer.Status);
            Assert.DoesNotContain("studio", answer.Body);
        });
    }

    /// <summary>Runs <paramref name="Command"/>, with <paramref name="Probe"/> between its step outside the transaction
    /// and the transaction itself: the instant after what it writes first and before its commit.</summary>
    private sealed record Between<T>(ICommand<T> Command, Func<CancellationToken, Task> Probe) : ICommand<T>
    {
        public async Task Prepare(CancellationToken ct)
        {
            await Command.Prepare(ct);
            await Probe(ct);
        }

        public Task<Outcome<T>> Run(Sequences sequences, CancellationToken ct) => Command.Run(sequences, ct);
    }
}
