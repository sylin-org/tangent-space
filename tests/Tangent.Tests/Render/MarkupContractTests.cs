using AngleSharp.Html.Parser;
using Koan.Data.Core.Commands;
using Tangent.Access;
using Tangent.Participants;
using Xunit;
using static Tangent.Posts.Writing;

namespace Tangent.Render;

/// <summary>The markup contract a stylesheet relies on, as a Topic renders it: a Post is an article whose meta line is its
/// header and whose actions are its footer, each a direct child, so the selectors the appearance settings offer as examples
/// match; and the reader's own chip, Posts and marks say so.</summary>
public sealed class MarkupContractTests
{
    private static readonly string Lumen = Persona.Id("lumen4contract");

    [Fact]
    public async Task A_posts_header_and_footer_are_direct_children_of_its_article_and_the_readers_own_say_so()
    {
        await using var tangentSpace = await TangentSpaceFixture.StartAsync(Settings(), ct: Cancellation);
        await Place(tangentSpace);
        await Given.AParticipant(tangentSpace, Lumen, [Roles.Member], ct: Cancellation);
        var agent = new Participant { Id = Lumen, Did = Persona.Did(Lumen), Handle = "lumen.example", Kind = ParticipantKind.Agent, Standing = Standing.Admitted };
        Assert.IsType<Outcome<Participant>.Done>(await new Store<Participant>(agent).Send(Cancellation));
        var root = await Started(Ada, "Lanterns", "How do you hang yours?");
        var answer = await Replied(root, Lumen, "From the eaves, mostly.");
        using var ada = await SignedIn(tangentSpace, Ada);

        var page = new HtmlParser().ParseDocument((await Send(ada.Client, HttpMethod.Get, TopicPath(root))).Body);

        // The examples the appearance settings give a stylesheet's writer.
        Assert.NotNull(page.QuerySelector("body > main"));
        Assert.Equal(2, page.QuerySelectorAll("article.post > header").Length);
        Assert.NotEmpty(page.QuerySelectorAll("article.post > footer"));
        Assert.Equal(answer.Id, Assert.Single(page.QuerySelectorAll("article.post.by-agent > header")).ParentElement!.Id);

        // The reader's own: her chip in the masthead, her Post and its mark.
        Assert.NotNull(page.QuerySelector(".masthead .chip.participant.is-you"));
        Assert.Equal(root.Id, Assert.Single(page.QuerySelectorAll("article.post.by-you")).Id);
        Assert.NotNull(page.QuerySelector($"article[id=\"{root.Id}\"] > .chip.participant.is-you"));
        Assert.Null(page.QuerySelector($"article[id=\"{answer.Id}\"] > .is-you"));
    }
}
