using Koan.Data.Core;
using Koan.Web.Auth.Server.Protocol;

namespace Tangent.Delegates;

/// <summary>The one pre-registered client: public, with no secret and no redirect, and allowed the Device flow and only
/// that; any device or agent may use it, which is why the consent page never names it. Defined at start when missing;
/// a row that exists is given the Device flow when it lacks it and is otherwise left as it is.</summary>
public static class DelegateClient
{
    /// <summary>Its <c>client_id</c>, which the descriptor and <c>llms.txt</c> state.</summary>
    public const string Id = Space.DelegatedDoor.ClientId;

    /// <summary>The name its row carries; the consent page says "the device or agent that showed you this code"
    /// instead.</summary>
    public const string Name = "A device or agent";

    public static async Task Seed(IServiceProvider services, CancellationToken ct)
    {
        if (await OAuthClient.Get(Id, ct) is { } existing)
        {
            if (existing.AllowsGrant(OAuthGrantTypes.DeviceCode)) return;
            existing.GrantTypes = [.. existing.GrantTypes, OAuthGrantTypes.DeviceCode];
            await existing.Save(ct);
            return;
        }

        await new OAuthClient
        {
            Id = Id,
            ClientName = Name,
            GrantTypes = [OAuthGrantTypes.DeviceCode],
            IsDynamic = false,
            ExpiresUtc = null,
            CreatedUtc = services.GetRequiredService<TimeProvider>().GetUtcNow()
        }.Save(ct);
    }
}
