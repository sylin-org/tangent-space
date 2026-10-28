using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Tangent.Community;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
using Tangent.Infrastructure;
namespace Tangent.Conversation;
public sealed class BlueskyBridge(IHttpClientFactory clients,TangentServer hub,TimeProvider clock,PolicyGate gate) {
 private readonly SemaphoreSlim refresh=new(1,1);
 public static string Key(string uri)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(uri)));
 public async Task<JsonDocument> Fetch(string method,IEnumerable<KeyValuePair<string,string>> parameters,CancellationToken ct) {
  var query=string.Join("&",parameters.Select(pair=>Uri.EscapeDataString(pair.Key)+"="+Uri.EscapeDataString(pair.Value)));
  using var response=await clients.CreateClient("bluesky-content").GetAsync("/xrpc/"+method+"?"+query,HttpCompletionOption.ResponseHeadersRead,ct);
  response.EnsureSuccessStatusCode();await response.Content.LoadIntoBufferAsync(1_000_000,ct);
  await using var body=await response.Content.ReadAsStreamAsync(ct);return await JsonDocument.ParseAsync(body,cancellationToken:ct);
 }
 public async Task Refresh(CancellationToken ct) {
  if(!await refresh.WaitAsync(0,ct))return;
  try {
   using var fresh=EntityContext.NoCache();var settings=await BlueskyBridgeSettings.Get("bluesky",ct);if(settings is null)return;
   var space=await Space.Get(TangentConstants.SpaceId,ct);
   if(space?.IsOwner(settings.ConfiguredBy)!=true||(await Participant.Get(settings.ConfiguredBy,ct))?.IsSuspended!=false)return;
   foreach(var flow in settings.Flows.Take(5))await ImportFlow(settings.ConfiguredBy,flow,ct);
   if(settings.BringReplies||settings.ShowCounts) {
    var publications=await BlueskyPublication.Query(row=>true,new QueryDefinition {Sort=SortSpecParser.ParseStrict<BlueskyPublication>("-PublishedAt,-Id")}.WithPagination(1,25),ct);
    foreach(var publication in publications) {
     var local=await Post.Get(publication.LocalPostId,ct);if(local is null||local.Removed)continue;
     var topic=await Topic.Get(local.TopicKey,ct);var tangent=topic is null?null:await Community.Tangent.Get(topic.TangentKey,ct);
     if(topic is null||!topic.IsPubliclyReadable(tangent)||(await hub.Topics.Describe(settings.ConfiguredBy,topic.Id,ct))?.CanManage!=true)continue;
     using var document=await Fetch("app.bsky.feed.getPostThread",new Dictionary<string,string>{{"uri",publication.SourceUri},{"depth","8"},{"parentHeight","0"}},ct);
     if(!document.RootElement.TryGetProperty("thread",out var thread)||!thread.TryGetProperty("post",out var root))continue;
     if(settings.ShowCounts)await new BlueskyReactionCounts {Id=local.Id,Likes=Count(root,"likeCount"),Reposts=Count(root,"repostCount"),FetchedAt=clock.GetUtcNow()}.Save(ct);
     if(settings.BringReplies) {var remaining=25;var queue=new Queue<(JsonElement Node,string Parent)>();if(thread.TryGetProperty("replies",out var replies))foreach(var reply in replies.EnumerateArray().Take(25))queue.Enqueue((reply,local.Id));
      while(queue.Count>0&&remaining-->0) {var (node,parent)=queue.Dequeue();if(!node.TryGetProperty("post",out var remote))continue;var imported=await Import(settings.ConfiguredBy,topic.Id,remote,parent,ct);if(imported is null)continue;if(node.TryGetProperty("replies",out var nested))foreach(var child in nested.EnumerateArray().Take(remaining))queue.Enqueue((child,imported.Id));}
     }
    }
   }
   await SetStatus("Refreshed",true,ct);
  }catch(Exception error) when(error is HttpRequestException or JsonException or ArgumentException or UnauthorizedAccessException or TangentRuleViolation) {
   await SetStatus("Refresh could not finish. Check the source and Tangent permissions, then retry.",false,ct);
  }finally{refresh.Release();}
 }
 private async Task SetStatus(string status,bool succeeded,CancellationToken ct) {
  await gate.Enter(ct);try {using var fresh=EntityContext.NoCache();var current=await BlueskyBridgeSettings.Get("bluesky",ct);if(current is null)return;current.Status=status;if(succeeded)current.RefreshedAt=clock.GetUtcNow();await current.Save(ct);}finally{gate.Exit();}
 }
 private async Task ImportFlow(string actor,BlueskyTopicFlow flow,CancellationToken ct) {
  var target=await hub.Tangents.Describe(actor,flow.TangentKey,ct);if(target?.CanManage!=true||!target.CanCreateTopic)return;
  using var data=await Fetch(flow.Kind=="feed"?"app.bsky.feed.getFeed":"app.bsky.feed.getAuthorFeed",new Dictionary<string,string>{{flow.Kind=="feed"?"feed":"actor",flow.Source},{"limit","25"}},ct);
  if(!data.RootElement.TryGetProperty("feed",out var feed))return;
  foreach(var entry in feed.EnumerateArray().Take(25)) {
   if(!entry.TryGetProperty("post",out var remote)||!remote.TryGetProperty("record",out var record)||record.TryGetProperty("reply",out _))continue;
   var uri=remote.GetProperty("uri").GetString()!;var id="source-"+Key(flow.TangentKey+"\n"+uri)[..24];
   var text=record.GetProperty("text").GetString()??"";if(string.IsNullOrWhiteSpace(text))continue;
   var title=text.Replace('\n',' ').Trim();if(title.Length>100)title=title[..100]+"…";
   if(await Topic.Get(id,ct) is null) {
    try{await hub.Tangents.CreateTopic(actor,flow.TangentKey,id,title,null,ct,TopicReadAudience.Public);}catch(TangentRuleViolation e) when(e.Denial==TangentDenial.AlreadyExists){}
   }
   await Import(actor,id,remote,null,ct);
  }
 }
 private async Task<Post?> Import(string actor,string topic,JsonElement remote,string? parent,CancellationToken ct) {
  var uri=remote.GetProperty("uri").GetString()!;var key=Key(topic+"\n"+uri);var mapped=await BlueskySourcePost.Get(key,ct);
  if(mapped is not null)return await Post.Get(mapped.PostId,ct);
  var author=remote.GetProperty("author");var did=author.GetProperty("did").GetString()!;
  var participant=await hub.Directory.EnsureAtproto(did,ct);
  var record=remote.GetProperty("record");var text=record.GetProperty("text").GetString()??"";
  if(string.IsNullOrWhiteSpace(text))return null;
  var created=DateTimeOffset.TryParse(record.GetProperty("createdAt").GetString(),out var date)?date:clock.GetUtcNow();
  var post=await hub.Posts.ImportSourcePost(actor,participant.Id,topic,"source-"+key,text,created,parent,uri,remote.GetProperty("cid").GetString()??"",ct);
  await new BlueskySourcePost {Id=key,SourceUri=uri,SourceCid=remote.GetProperty("cid").GetString()??"",PostId=post.Id,TopicKey=topic}.Save(ct);
  return post;
 }
 private static int Count(JsonElement post,string key)=>post.TryGetProperty(key,out var count)&&count.TryGetInt32(out var value)?Math.Max(0,value):0;
}
public sealed class BlueskyBridgeWorker(BlueskyBridge bridge,ILogger<BlueskyBridgeWorker> logger):BackgroundService {
 protected override async Task ExecuteAsync(CancellationToken ct){while(!ct.IsCancellationRequested){try{await Task.Delay(TimeSpan.FromMinutes(10),ct);await bridge.Refresh(ct);}catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}catch(Exception error){logger.LogWarning(error,"Bluesky source refresh failed");}}}
}

