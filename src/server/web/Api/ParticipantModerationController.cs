using Koan.Data.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Access;
using Tangent.Application;
using Tangent.Community;
using Tangent.Identity;
using Tangent.Infrastructure;
using Tangent.Spaces;
using Tangent.Stewardship;
namespace Tangent.Api;
[ApiController,Authorize,Route("api/people/{identifier}/moderation")]
public sealed class ParticipantModerationController(TangentServer hub,UserManagementService users,TimeProvider clock):ControllerBase {
 private async Task<(string Actor,Participant Target,List<string> Actions,ScopedRestriction? Restriction)> Context(string identifier,string? tangent,CancellationToken ct) {
  var actor=ParticipationAccess.Require(User,ParticipationGrants.Manage);
  var space=await Space.Get(TangentConstants.SpaceId,ct);
  if((await Participant.Get(actor,ct)) is not {IsSuspended:false} || !await SpaceAdmission.Admitted(space,actor,ct))throw new UnauthorizedAccessException();
  var target=(await hub.Directory.ByIdentifier(identifier,ct))?.Participant??throw new ArgumentException("That participant has not arrived here.");
  var actions=new List<string>();ScopedRestriction? restriction=null;
  if(actor==target.Id||space?.IsOwner(target.Id)==true)return(actor,target,actions,restriction);
  var bag=await hub.Roles.Bag(actor,ct);
  if(TangentRoleAccess.CanDo([TangentPermissions.WarnUsers],bag))actions.Add("warn");
  if(TangentRoleAccess.CanDo([TangentPermissions.ManageParticipants],bag))actions.Add(target.IsSuspended?"restore":"suspend");
  if(!string.IsNullOrEmpty(tangent)) {
   var scope=await Tangent.Community.Tangent.Get(tangent,ct);
   var membership=await TangentMembership.Get(TangentMembership.Key(tangent,actor),ct);
   if(scope is not null && !scope.IsOwner(target.Id) && (space?.IsOwner(actor)==true||scope.IsOwner(actor)||membership?.Role==TangentRole.Admin) && await Restrictions.ForTangent(actor,tangent,clock.GetUtcNow(),ct) is null) {
    actions.AddRange(["timeout","ban","lift"]);
    restriction=await ScopedRestriction.Get(ScopedRestriction.Key(RestrictionScope.Tangent,tangent,target.Id),ct);
   }
  }
  return(actor,target,actions,restriction);
 }
 [HttpGet]public async Task<IActionResult> Read(string identifier,[FromQuery]string? tangent,CancellationToken ct){Response.Headers.CacheControl="no-store";try{using var fresh=EntityContext.NoCache();var context=await Context(identifier,tangent,ct);var r=context.Restriction;var available = string.IsNullOrEmpty(tangent) ? await hub.Tangents.List(context.Actor,ct) : null;var scopeActions=available?.Tangents.Any(item=>item.IsOwner||item.MembershipRole==TangentRole.Admin)==true && context.Target.Id!=context.Actor && (await Space.Get(TangentConstants.SpaceId,ct))?.IsOwner(context.Target.Id)!=true ? new[]{"timeout","ban"}:Array.Empty<string>();return Ok(new {actions=context.Actions,scopeActions,restriction=r is null?null:new {kind=r.Kind.ToString(),r.Until,r.Reason,r.BlockPosting,r.BlockStarting,active=Restrictions.Evaluate(r,clock.GetUtcNow()) is not null}});}catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException e){return BadRequest(new {message=e.Message});}}
 [HttpPost,TopicMutation(ParticipationGrants.Manage)]public async Task<IActionResult> Apply(string identifier,ParticipantModerationPatch input,CancellationToken ct){try{using var fresh=EntityContext.NoCache();var context=await Context(identifier,input.Tangent,ct);if(!context.Actions.Contains(input.Action))return Forbid();var note=string.IsNullOrWhiteSpace(input.Note)?input.Action=="warn"?"":$"{input.Action} by a steward.":input.Note.Trim();
  if(input.Action is "timeout" or "ban" or "lift") {
   DateTimeOffset? until=null;if(input.Action=="timeout"){if(input.DurationSeconds is not { } seconds || seconds<60 || seconds>30*86400)return BadRequest(new {message="Choose a time out from one minute to 30 days."});until=clock.GetUtcNow().AddSeconds(seconds);}
   await hub.Participants.SetRestriction(context.Actor,input.Tangent!,null,identifier,input.Action=="timeout"?RestrictionKind.Timeout:input.Action=="ban"?RestrictionKind.Ban:RestrictionKind.None,until,note,ct,input.BlockPosting,input.BlockStarting);
  }else await users.Manage(context.Actor,identifier,input.Action,note,null,null,null,ct);
  return Ok(new {saved=true});
 }catch(UnauthorizedAccessException){return Forbid();}catch(ArgumentException e){return BadRequest(new {message=e.Message});}catch(TangentRuleViolation e){return StatusCode(e.Denial==TangentDenial.Forbidden?403:400,new {message=e.Message});}}
}
public sealed record ParticipantModerationPatch(string Action,string? Tangent=null,string Note="",long? DurationSeconds=null,bool BlockPosting=true,bool BlockStarting=true);
