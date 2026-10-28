using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Application;
using Tangent.Conversation;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;
namespace Tangent.Api;
[ApiController,Authorize,Route("api/v1/content/bluesky/bridge")]
public sealed class BlueskyBridgeController(BlueskyBridge bridge,PolicyGate gate,TangentServer hub):ControllerBase {
 private async Task<string> Owner(CancellationToken ct){Response.Headers.CacheControl="no-store";var actor=ParticipationAccess.Require(User,ParticipationGrants.Manage);if((await Space.Get(TangentConstants.SpaceId,ct))?.IsOwner(actor)!=true||(await Participant.Get(actor,ct))?.IsSuspended!=false)throw new UnauthorizedAccessException();return actor;}
 [HttpGet]public async Task<IActionResult> Read(CancellationToken ct){try{await Owner(ct);return Ok(await BlueskyBridgeSettings.Get("bluesky",ct)??new BlueskyBridgeSettings {Id="bluesky"});}catch(UnauthorizedAccessException){return Forbid();}}
 [HttpPatch,TopicMutation(ParticipationGrants.Manage)]public async Task<IActionResult> Save(BridgePatch input,CancellationToken ct){await gate.Enter(ct);try{using var fresh=EntityContext.NoCache();var actor=await Owner(ct);if(input.Flows.Count>5)throw new ArgumentException("Use up to five account or feed flows.");foreach(var flow in input.Flows){if(flow.Kind is not("account" or "feed")||flow.Source.Length is <3 or >2048||flow.Source.Any(char.IsControl))throw new ArgumentException("Choose an account handle, DID or feed URI.");if(flow.Kind=="feed"&&(!flow.Source.StartsWith("at://did:",StringComparison.Ordinal)||!flow.Source.Contains("/app.bsky.feed.generator/",StringComparison.Ordinal)))throw new ArgumentException("Use the feed’s at:// URI.");if((await hub.Tangents.Describe(actor,flow.TangentKey,ct)) is not {CanManage:true,CanCreateTopic:true})throw new UnauthorizedAccessException();}var settings=await BlueskyBridgeSettings.Get("bluesky",ct)??new BlueskyBridgeSettings {Id="bluesky"};settings.BringReplies=input.BringReplies;settings.ShowCounts=input.ShowCounts;settings.Flows=input.Flows;settings.ConfiguredBy=actor;await settings.Save(ct);return Ok(settings);}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException e){return BadRequest(new {message=e.Message});}finally{gate.Exit();}}
 [HttpPost("refresh"),TopicMutation(ParticipationGrants.Manage)]public async Task<IActionResult> Refresh(CancellationToken ct){try{await Owner(ct);await bridge.Refresh(ct);return Ok(await BlueskyBridgeSettings.Get("bluesky",ct));}catch(UnauthorizedAccessException){return Forbid();}}
 [HttpGet("counts/{postId}"),AllowAnonymous]public async Task<IActionResult> Counts(string postId,CancellationToken ct){string? actor=null;if(User.Identity?.IsAuthenticated==true){try{actor=ParticipationAccess.Require(User,ParticipationGrants.Read);}catch(UnauthorizedAccessException){return Unauthorized();}}Response.Headers.CacheControl="private,no-store";Response.Headers.Vary="Cookie, Authorization";if((await BlueskyBridgeSettings.Get("bluesky",ct))?.ShowCounts!=true)return Ok(null);var post=await Post.Get(postId,ct);if(post is null||post.Removed||(await hub.Topics.Describe(actor,post.TopicKey,ct))?.CanRead!=true)return NotFound();return Ok(await BlueskyReactionCounts.Get(postId,ct));}
}
public sealed record BridgePatch(bool BringReplies,bool ShowCounts,List<BlueskyTopicFlow> Flows);
