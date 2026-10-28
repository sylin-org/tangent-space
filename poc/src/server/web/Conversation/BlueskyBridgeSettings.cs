using Koan.Data.Core.Model;
namespace Tangent.Conversation;
public sealed class BlueskyBridgeSettings : Entity<BlueskyBridgeSettings> {
 public bool BringReplies {get;set;}
 public bool ShowCounts {get;set;}
 public List<BlueskyTopicFlow> Flows {get;set;}=[];
 public string ConfiguredBy {get;set;}="";
 public DateTimeOffset? RefreshedAt {get;set;}
 public string Status {get;set;}="";
}
public sealed record BlueskyTopicFlow(string Kind,string Source,string TangentKey);
public sealed class BlueskySourcePost : Entity<BlueskySourcePost> {
 public string SourceUri {get;set;}="";
 public string PostId {get;set;}="";
 public string TopicKey {get;set;}="";
 public string SourceCid {get;set;}="";
}
public sealed class BlueskyReactionCounts : Entity<BlueskyReactionCounts> {
 public int Likes {get;set;}
 public int Reposts {get;set;}
 public DateTimeOffset FetchedAt {get;set;}
}
