using System.Text.Json.Serialization;

namespace Tangent.Conversation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed record PostCreateRequest(string OperationId, string Text, SourceReference? ReplyTo = null,
    IReadOnlyList<PostFacet>? Facets = null);
