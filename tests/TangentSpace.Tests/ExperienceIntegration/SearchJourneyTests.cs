using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Community;
using Tangent.Conversation;
using Xunit;

namespace Tangent.Tests.ExperienceIntegration;

[Collection("Experience integration")]
public sealed class SearchJourneyTests
{
    [Fact]
    public async Task Search_finds_case_variants_titles_and_replies_without_exposing_private_posts()
    {
        await using var app = await ExperienceWebApp.StartAsync();
        var ct = TestContext.Current.CancellationToken;
        var topic = (await Topic.Get(ExperienceWebApp.TopicKey, ct))!;
        topic.Title = "What Kind of Discussions?";
        await topic.Save(ct);
        var reply = (await Post.Get("m-leo-r1", ct))!;
        reply.Content = reply.Content with { Text = "A MixedCase Needle in a reply." };
        await reply.Save(ct);

        foreach (var query in new[] { "mixedcase needle", "MIXEDCASE NEEDLE", "MixedCase Needle" })
        {
            var result = await app.Http.GetFromJsonAsync<JsonElement>("/api/search?q=" + Uri.EscapeDataString(query), ct);
            Assert.Contains(result.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == reply.Id);
        }
        var title = await app.Http.GetFromJsonAsync<JsonElement>("/api/search?q=what%20kind", ct);
        Assert.Contains(title.GetProperty("items").EnumerateArray(), item => item.GetProperty("title").GetString() == topic.Title);

        await using (var database = new SqliteConnection(app.Services.GetRequiredService<IConfiguration>()["Koan:Data:Sqlite:ConnectionString"]))
        {
            await database.OpenAsync(ct);
            await using var command = database.CreateCommand();
            command.CommandText = "UPDATE \"Tangent.Conversation.Post\" SET \"Json\" = json_remove(\"Json\", '$.SearchText', '$.searchText') WHERE \"Id\" = $id";
            command.Parameters.AddWithValue("$id", reply.Id);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(ct));
        }
        using (EntityContext.NoCache())
            Assert.Null((await Post.Get(reply.Id, ct))!.SearchText);
        await PostSearch.Prepare(ct);
        var indexed = await app.Http.GetFromJsonAsync<JsonElement>("/api/search?q=mixedcase%20needle", ct);
        Assert.Contains(indexed.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == reply.Id);

        using var visitor = new HttpClient { BaseAddress = new Uri(app.Origin) };
        foreach (var query in new[] { "mixedcase", "what kind" })
        {
            var result = await visitor.GetFromJsonAsync<JsonElement>("/api/search?q=" + Uri.EscapeDataString(query), ct);
            Assert.Empty(result.GetProperty("items").EnumerateArray());
        }
        reply.Removed = true;
        await reply.Save(ct);
        var removed = await app.Http.GetFromJsonAsync<JsonElement>("/api/search?q=mixedcase", ct);
        Assert.Empty(removed.GetProperty("items").EnumerateArray());
    }
}
