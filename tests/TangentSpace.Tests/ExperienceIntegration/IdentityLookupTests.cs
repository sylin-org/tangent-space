using Koan.Data.Core;
using System.Text.Json;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class IdentityLookupTests
{
    [Fact]
    public async Task Mention_suggestions_query_stored_prefixes_and_exclude_unrelated_people()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var tangent = (await Community.Tangent.Get(ExperienceWebApp.TangentKey, ct))!;
        var memberId = "";
        foreach (var label in new[] { "lookup.member", "lookup.outsider" })
        {
            var participant = new Participant { Id = Participant.NewIdentifier() };
            await participant.Save(ct);
            await ParticipantIdentity.Internal(participant.Id, DateTimeOffset.UtcNow).Save(ct);
            await new ParticipantIdentity
            {
                Id = ParticipantIdentity.Key("test", label), Kind = "test", Value = label,
                ParticipantId = participant.Id, Label = label
            }.Save(ct);
            if (label == "lookup.member")
            {
                memberId = participant.Id;
                await TangentMembership.Assign(tangent, participant.Id, TangentRole.Member,
                    app.OwnerParticipantId, DateTimeOffset.UtcNow).Save(ct);
            }
        }
        async Task<JsonDocument> Read(string prefix)
        {
            using var response = await app.Http.GetAsync($"/api/v1/experience/topics/{ExperienceWebApp.TopicKey}/mentionables?prefix={prefix}", ct);
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        using var matches = await Read("LOOKUP.");
        var target = Assert.Single(matches.RootElement.GetProperty("targets").EnumerateArray());
        Assert.Equal(ParticipantIdentity.InternalValue(memberId), target.GetProperty("did").GetString());
        using var outsider = await Read("lookup.outsider");
        Assert.Empty(outsider.RootElement.GetProperty("targets").EnumerateArray());
        using var groups = await Read("members");
        // The seeded agent and the new local member are both counted without hydrating either.
        Assert.Equal(2, Assert.Single(groups.RootElement.GetProperty("targets").EnumerateArray()).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Indexed_handle_resolution_preserves_spelling_relabeling_and_distinct_holder_ambiguity()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var first = new ParticipantIdentity
        {
            Id = ParticipantIdentity.Key("test", "first"), Kind = "test", Value = "first",
            ParticipantId = app.HumanParticipantId, Label = "  MiXeD.Test  "
        };
        var second = new ParticipantIdentity
        {
            Id = ParticipantIdentity.Key("test", "second"), Kind = "test", Value = "second",
            ParticipantId = app.HumanParticipantId, Label = "mixed.test"
        };
        await first.Save(ct);
        await second.Save(ct);
        var match = await ParticipantLookup.TryResolveByIdentifier("@MIXED.TEST", ct);
        Assert.Equal(app.HumanParticipantId, match?.Participant.Id);
        Assert.Equal("  MiXeD.Test  ", (await ParticipantIdentity.Get(first.Id, ct))!.Label);
        var facets = await PostFacets.Effective("Hello @mixed.test\n```\n@agent.experience.test\n```", null, ct);
        Assert.Single(facets);
        Assert.Equal(ExperienceWebApp.HumanDid, facets[0].Did);

        var collision = new ParticipantIdentity
        {
            Id = ParticipantIdentity.Key("test", "third"), Kind = "test", Value = "third",
            ParticipantId = app.AgentParticipantId, Label = "Mixed.Test"
        };
        await collision.Save(ct);
        Assert.Null(await ParticipantLookup.TryResolveByIdentifier("mixed.test", ct));
        Assert.Empty(await PostFacets.Effective("Hello @mixed.test", null, ct));
        collision.Relabel("separate.test");
        await collision.Save(ct);
        Assert.Equal(app.HumanParticipantId, (await ParticipantLookup.TryResolveByIdentifier("mixed.test", ct))?.Participant.Id);
        Assert.Equal(app.AgentParticipantId, (await ParticipantLookup.TryResolveByIdentifier("separate.test", ct))?.Participant.Id);
        first.Relabel("admins");
        await first.Save(ct);
        Assert.Empty(await PostFacets.Effective("Hello @admins.example", null, ct));
        first.Relabel("unclaimed.test");
        await first.Save(ct);
        Assert.Equal(PostFacet.Group, Assert.Single(await PostFacets.Effective("Hello @admins.example", null, ct)).Kind);
    }
}
