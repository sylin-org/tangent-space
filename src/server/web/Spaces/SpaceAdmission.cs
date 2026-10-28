using Koan.Data.Core.Model;
using Tangent.Identity;
namespace Tangent.Spaces;
public sealed class SpaceMembership : Entity<SpaceMembership> {
    public string Status {get;set;}="pending";
    public DateTimeOffset RequestedAt {get;set;}
    public DateTimeOffset? DecidedAt {get;set;}
    public string? DecidedBy {get;set;}
}
public static class SpaceAdmission {
    public static async Task<bool> Admitted(Space? space,string? actor,CancellationToken ct) {
        if(space is null||actor is null)return false;
        if(space.IsOwner(actor))return true;
        var member=await SpaceMembership.Get(actor,ct);
        if(member is {Status:"refused"})return false;
        if(space.Admission=="Open"||member is {Status:"accepted"})return true;
        var participant=await Participant.Get(actor,ct);
        return participant is not null && participant.JoinedAt<space.AdmissionChangedAt;
    }
}
