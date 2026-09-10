namespace TangentSpace.Conversation;

[Newtonsoft.Json.JsonObject(MissingMemberHandling = Newtonsoft.Json.MissingMemberHandling.Error)]
public sealed record SourceReference(string Uri, string Cid);
