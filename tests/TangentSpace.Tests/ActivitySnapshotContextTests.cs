using System.Text.Json;
using TangentSpace.Activity;
using Xunit;

namespace TangentSpace.Tests;

public sealed class ActivitySnapshotContextTests
{
    [Fact]
    public void Browser_snapshot_actor_context_uses_the_same_web_json_shape_as_stream_frames()
    {
        var source = new ActivitySnapshot("opaque-checkpoint", [], null, false, false, []);
        var browser = source with { ParticipantRef = "current-participant" };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(browser, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal("current-participant", document.RootElement.GetProperty("participantRef").GetString());
        Assert.Equal("opaque-checkpoint", document.RootElement.GetProperty("checkpoint").GetString());
        Assert.Null(source.ParticipantRef); // Adding response context does not alter the underlying snapshot.
    }
}
