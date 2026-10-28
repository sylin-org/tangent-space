using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Koan.Data.Core;
using Microsoft.Extensions.DependencyInjection;
using Tangent;
using Tangent.Access;
using Tangent.Activity;
using Tangent.Application;
using Tangent.Community;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Media;
using Tangent.Spaces;
using Xunit;
namespace Tangent.Tests.ExperienceIntegration;
[Collection("Experience integration")]
public sealed class RedesignWorkflowTests {
 [Fact]
 public async Task Workspace_admission_search_storage_and_moderation_apply_current_policy() {
  await using var app=await ExperienceWebApp.StartAsync();var ct=TestContext.Current.CancellationToken;
  using var owner=new HttpClient {BaseAddress=new Uri(app.Origin)};owner.DefaultRequestHeaders.Authorization=new("Bearer",app.OwnerToken);
  using var anonymous=new HttpClient {BaseAddress=new Uri(app.Origin)};
  var hub=app.Services.GetRequiredService<TangentServer>();
  var preferences=await owner.PatchAsJsonAsync("/api/workspace/preferences",new {theme="light",repliesPlacement="below",notifyMentions=false},ct);preferences.EnsureSuccessStatusCode();
  var prefs=await owner.GetFromJsonAsync<JsonElement>("/api/workspace/preferences",ct);Assert.Equal("light",prefs.GetProperty("theme").GetString());Assert.Equal("below",prefs.GetProperty("repliesPlacement").GetString());Assert.False(prefs.GetProperty("notifyMentions").GetBoolean());
  await hub.Participants.SetWatch(app.OwnerParticipantId,ExperienceWebApp.TopicKey,WatchMode.All,ct);await hub.Participants.SetTangentWatch(app.OwnerParticipantId,ExperienceWebApp.TangentKey,WatchMode.All,ct);
  var followed=await owner.GetFromJsonAsync<JsonElement>("/api/workspace/following",ct);Assert.Equal(2,followed.GetProperty("items").GetArrayLength());
  var search=await owner.GetAsync("/api/search?q=summary",ct);Assert.Equal(HttpStatusCode.OK,search.StatusCode);var hits=JsonDocument.Parse(await search.Content.ReadAsStringAsync(ct));Assert.Contains(hits.RootElement.GetProperty("items").EnumerateArray(),item=>item.GetProperty("id").GetString()=="m-leo-r1");
  var privateSearch=await anonymous.GetFromJsonAsync<JsonElement>("/api/search?q=summary",ct);Assert.Empty(privateSearch.GetProperty("items").EnumerateArray());
  var storage=await owner.PatchAsJsonAsync("/api/media/storage",new {location="local",maximumUploadMb=1,resizeImages=true,removeLocationData=true},ct);storage.EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await app.Http.GetAsync("/api/media/storage",ct)).StatusCode);
  var uploaded=await owner.PostAsJsonAsync("/api/media",new {tangentKey=ExperienceWebApp.TangentKey,topicKey=ExperienceWebApp.TopicKey,fileName="test.png",content=TestImage.Png()},ct);uploaded.EnsureSuccessStatusCode();var upload=JsonDocument.Parse(await uploaded.Content.ReadAsStringAsync(ct));Assert.Equal("image/webp",upload.RootElement.GetProperty("contentType").GetString());
  using(var copy=await owner.GetAsync("/api/media/storage/download",ct)){copy.EnsureSuccessStatusCode();using var zip=new ZipArchive(new MemoryStream(await copy.Content.ReadAsByteArrayAsync(ct)));Assert.Contains(zip.Entries,e=>e.FullName.StartsWith("images/"));Assert.Contains(zip.Entries,e=>e.FullName.StartsWith("metadata/"));}
  Assert.Equal(HttpStatusCode.Forbidden,(await app.Http.GetAsync("/api/media/storage/download",ct)).StatusCode);
  var path="/api/people/"+Uri.EscapeDataString(ExperienceWebApp.AgentHandle)+"/moderation";
  var timeout=await owner.PostAsJsonAsync(path,new {action="timeout",tangent=ExperienceWebApp.TangentKey,note="Take some time",durationSeconds=3600,blockPosting=false,blockStarting=true},ct);timeout.EnsureSuccessStatusCode();Assert.True((await hub.Topics.Describe(app.AgentParticipantId,ExperienceWebApp.TopicKey,ct))!.CanWrite);Assert.False((await hub.Tangents.Describe(app.AgentParticipantId,ExperienceWebApp.TangentKey,ct))!.CanCreateTopic);
  (await owner.PostAsJsonAsync(path,new {action="lift",tangent=ExperienceWebApp.TangentKey,note="Welcome back"},ct)).EnsureSuccessStatusCode();Assert.True((await hub.Tangents.Describe(app.AgentParticipantId,ExperienceWebApp.TangentKey,ct))!.CanCreateTopic);
  (await owner.PatchAsJsonAsync("/api/space/admission",new {mode="Approval"},ct)).EnsureSuccessStatusCode();Assert.True((await hub.Topics.Describe(app.AgentParticipantId,ExperienceWebApp.TopicKey,ct))!.CanWrite);
  var (visitor,identities)=Participant.Enroll("did:plc:approvalvisitorAAAAAA","new.experience.test",DateTimeOffset.UtcNow);await visitor.Save(ct);foreach(var identity in identities)await identity.Save(ct);
  var (credential,token)=ParticipantCredential.Issue(visitor.Id,"Admission check",1,[ParticipationGrants.Read,ParticipationGrants.Post],DateTimeOffset.UtcNow);await credential.Save(ct);
  using var newcomer=new HttpClient {BaseAddress=new Uri(app.Origin)};newcomer.DefaultRequestHeaders.Authorization=new("Bearer",token);
  var arrival=await newcomer.GetFromJsonAsync<JsonElement>("/api/space/admission",ct);Assert.False(arrival.GetProperty("admitted").GetBoolean());
  (await newcomer.PostAsJsonAsync("/api/space/admission/join",new {},ct)).EnsureSuccessStatusCode();var request=await owner.GetFromJsonAsync<JsonElement>("/api/space/admission/requests",ct);Assert.Contains(request.GetProperty("items").EnumerateArray(),item=>item.GetProperty("id").GetString()==visitor.Id);
  await Assert.ThrowsAsync<TangentRuleViolation>(()=>hub.Tangents.CreateTopic(visitor.Id,ExperienceWebApp.TangentKey,"not-admitted","Not admitted",null,ct));
  (await owner.PostAsJsonAsync("/api/space/admission/requests/"+visitor.Id,new {accept=true},ct)).EnsureSuccessStatusCode();Assert.True((await newcomer.GetFromJsonAsync<JsonElement>("/api/space/admission",ct)).GetProperty("admitted").GetBoolean());
 }
 [Fact]
 public async Task Bluesky_flows_import_attributed_posts_and_replies_once_and_keep_counts_private() {
  await using var app=await ExperienceWebApp.StartAsync();var ct=TestContext.Current.CancellationToken;var hub=app.Services.GetRequiredService<TangentServer>();
  await hub.Tangents.SetAccess(app.OwnerParticipantId,ExperienceWebApp.TangentKey,new AccessMap {See=[AccessTokens.Everyone],Post=["role:member"],CreateTopics=["role:member"]},ct);
  Assert.True((await hub.Topics.SetReadAudience(app.OwnerParticipantId,ExperienceWebApp.TopicKey,TopicReadAudience.Public,true,ct)).Accepted);
  await new BlueskyBridgeSettings {Id="bluesky",ConfiguredBy=app.OwnerParticipantId,BringReplies=true,ShowCounts=true,Flows=[new("account",ExperienceWebApp.AgentHandle,ExperienceWebApp.TangentKey)]}.Save(ct);
  const string uri="at://did:plc:experienceagentAAAAAA/app.bsky.feed.post/3abc";
  await new BlueskyPublication {Id="publication-fixture",LocalPostId="m-leo-r1",SourceUri=uri,PublishedAt=DateTimeOffset.UtcNow}.Save(ct);
  var remote=new {uri,cid="bafysource",author=new {did=ExperienceWebApp.AgentDid},record=new {text="A source thought",createdAt="2026-09-29T10:00:00Z"},likeCount=7,repostCount=3};
  var reply=new {uri="at://did:plc:experiencehumanAAAAAA/app.bsky.feed.post/3reply",cid="bafyreply",author=new {did=ExperienceWebApp.HumanDid},record=new {text="A source reply",createdAt="2026-09-29T11:00:00Z"}};
  var bridge=new BlueskyBridge(new FixtureClients(JsonSerializer.Serialize(new {feed=new[]{new {post=remote}}}),JsonSerializer.Serialize(new {thread=new {post=remote,replies=new[]{new {post=reply}}}})),hub,TimeProvider.System,app.Services.GetRequiredService<PolicyGate>());
  await bridge.Refresh(ct);await bridge.Refresh(ct);
  var sourceTopic="source-"+BlueskyBridge.Key(ExperienceWebApp.TangentKey+"\n"+uri)[..24];var posts=await Post.Query(p=>p.TopicKey==sourceTopic,ct);Assert.Single(posts);Assert.Equal(app.AgentParticipantId,posts[0].AuthorParticipantId);Assert.Equal(uri,posts[0].SourceUri);
  var replies=await Post.Query(p=>p.TopicKey==ExperienceWebApp.TopicKey&&p.SourceUri==reply.uri,ct);Assert.Single(replies);Assert.Equal(app.HumanParticipantId,replies[0].AuthorParticipantId);Assert.Equal("m-leo-r1",replies[0].Content.ReplyTo);
  using var anonymous=new HttpClient {BaseAddress=new Uri(app.Origin)};var counts=await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/content/bluesky/bridge/counts/m-leo-r1",ct);Assert.Equal(7,counts.GetProperty("likes").GetInt32());
  await hub.Tangents.SetAccess(app.OwnerParticipantId,ExperienceWebApp.TangentKey,new AccessMap {See=[]},ct);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync("/api/v1/content/bluesky/bridge/counts/m-leo-r1",ct)).StatusCode);
 }
 [Fact]
 public async Task Tangent_audience_and_article_subtitle_are_explicit_and_request_replays_preserve_them() {
  await using var app=await ExperienceWebApp.StartAsync();var ct=TestContext.Current.CancellationToken;
  using var owner=new HttpClient {BaseAddress=new Uri(app.Origin)};owner.DefaultRequestHeaders.Authorization=new("Bearer",app.OwnerToken);
  var created=await owner.PostAsJsonAsync("/api/v1/tangents",new {key="field-notes",name="Field Notes",description="",motto="",accent="#c8931f",artwork="",kind="articles",readAudience="Public"},ct);created.EnsureSuccessStatusCode();
  var card=await owner.GetFromJsonAsync<JsonElement>("/api/v1/tangents/field-notes",ct);Assert.Equal("Public",card.GetProperty("readAudience").GetString());
  var request=new {requestId="subtitle-request",title="A considered place",subtitle="Small things noticed together",text="## Begin\n\nAn opening thought.",format="markdown",audience="Public"};
  var started=await owner.PostAsJsonAsync("/api/v1/experience/tangents/field-notes/topics",request,ct);started.EnsureSuccessStatusCode();var result=JsonDocument.Parse(await started.Content.ReadAsStringAsync(ct));var key=result.RootElement.GetProperty("topicRef").GetString()!.Split("::")[^1];
  Assert.Equal(request.subtitle,(await Topic.Get(key,ct))!.Description);
  (await owner.PostAsJsonAsync("/api/v1/experience/tangents/field-notes/topics",request,ct)).EnsureSuccessStatusCode();
  var changed=await owner.PostAsJsonAsync("/api/v1/experience/tangents/field-notes/topics",new {request.requestId,request.title,subtitle="A changed intent",request.text,request.format,request.audience},ct);Assert.Equal(HttpStatusCode.Conflict,changed.StatusCode);
 }
 private sealed class FixtureClients(string feed,string thread):IHttpClientFactory { public HttpClient CreateClient(string name)=>new(new Handler(feed,thread)){BaseAddress=new Uri("https://public.api.bsky.app")}; }
 private sealed class Handler(string feed,string thread):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(request.RequestUri!.AbsolutePath.EndsWith("getPostThread")?thread:feed,System.Text.Encoding.UTF8,"application/json")});}
}
