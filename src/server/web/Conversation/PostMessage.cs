using System.Text.Json.Serialization;

namespace TangentSpace.Conversation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed record PostMessage(string OperationId, string Text, SourceReference? ReplyTo = null,
    IReadOnlyList<PostFacet>? Facets = null);
