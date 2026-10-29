using Koan.Identity.Roles;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Participants;
using Tangent.Posts;
using Tangent.Space;
using Tangent.Tangents;
using Tangent.Writer;
using Xunit;

namespace Tangent;

/// <summary>The rows a proof starts from, written the way every row is written: a Tangent with its audience as its
/// everyone overwrite, and a participant with a standing and the roles they hold on Koan's RoleCollection.</summary>
public static class Given
{
    public static async Task<Tangents.Tangent> ATangent(TangentSpaceFixture tangentSpace, string slug,
        Audience audience = Audience.Public, CancellationToken ct = default)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var tangent = Tangents.Tangent.Create(slug, slug, "", PostKind.Topic, 1);
        Assert.IsType<Outcome<Tangents.Tangent>.Done>(await writer.Send(new Store<Tangents.Tangent>(tangent), ct));
        Assert.IsType<Outcome<Overwrite>.Done>(await writer.Send(new Store<Overwrite>(Overwrite.ForAudience(slug, audience)), ct));
        return tangent;
    }

    public static async Task<Participant> AParticipant(TangentSpaceFixture tangentSpace, string id, IReadOnlyList<string> roles,
        Standing standing = Standing.Admitted, CancellationToken ct = default)
    {
        var writer = tangentSpace.Services.GetRequiredService<WriteLoop>();
        var participant = new Participant { Id = id, Handle = id + ".example", Standing = standing };
        Assert.IsType<Outcome<Participant>.Done>(await writer.Send(new Store<Participant>(participant), ct));
        var collection = tangentSpace.Services.GetRequiredService<RoleCollection>();
        foreach (var role in roles) await collection.Add(role, id, ct);
        return participant;
    }
}
