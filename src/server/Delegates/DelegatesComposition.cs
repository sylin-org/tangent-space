using Koan.Web.Auth.Server.Options;
using Microsoft.AspNetCore.Mvc;

namespace Tangent.Delegates;

/// <summary>What the delegated agent door needs composed beside Koan's OAuth server, which referencing it activates
/// with the base URL as its issuer, and Koan's resource edge, which <c>Koan:Web:Auth:ResourceServer:Enabled</c> turns
/// on: the consent and done pages at <see cref="ConsentController"/>'s paths, and Koan's approve and deny under the one
/// antiforgery rule (<see cref="ConsentForms"/>).</summary>
public static class DelegatesComposition
{
    public static IServiceCollection AddDelegates(this IServiceCollection services)
    {
        services.AddOptions<AuthServerOptions>().PostConfigure(server =>
        {
            server.ConsentPath = ConsentController.Path;
            server.DonePath = ConsentController.DonePath;
        });
        services.AddOptions<MvcOptions>().Configure(mvc => mvc.Filters.Add<ConsentForms>());
        return services;
    }
}
