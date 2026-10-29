using Koan.Security.Trust.Inbound;
using Koan.Web.Auth.Server.Options;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;

namespace Tangent.Delegates;

/// <summary>What the delegated agent door needs composed beside Koan's OAuth server, which referencing it activates
/// with the base URL as its issuer: the consent and done pages at <see cref="ConsentController"/>'s paths; every
/// request authenticated once by <see cref="DelegateIdentity.Scheme"/>, with a bearer accepted only for this Tangent
/// Space; a page asked with a rejected bearer answering so (<see cref="RejectedBearer"/>); and Koan's approve and deny
/// under the one antiforgery rule (<see cref="ConsentForms"/>).</summary>
public static class DelegatesComposition
{
    public static IServiceCollection AddDelegates(this IServiceCollection services)
    {
        services.AddOptions<AuthServerOptions>().PostConfigure(server =>
        {
            server.ConsentPath = ConsentController.Path;
            server.DonePath = ConsentController.DonePath;
        });

        services.AddAuthentication()
            .AddPolicyScheme(DelegateIdentity.Scheme, null, policy => policy.ForwardDefaultSelector = DelegateIdentity.Select);
        services.PostConfigure<AuthenticationOptions>(authentication => authentication.DefaultAuthenticateScheme = DelegateIdentity.Scheme);
        services.AddOptions<JwtBearerOptions>(KoanBearerDefaults.AuthenticationScheme)
            .PostConfigure<KoanWebBaseUrl>((bearer, baseUrl) =>
            {
                bearer.Events ??= new JwtBearerEvents();
                var verified = bearer.Events.OnTokenValidated;
                bearer.Events.OnTokenValidated = async context =>
                {
                    await verified(context);
                    if (context.Result is null) await DelegateIdentity.Admit(context, baseUrl);
                };
            });

        services.AddOptions<MvcOptions>().Configure(mvc =>
        {
            mvc.Filters.Add<RejectedBearer>();
            mvc.Filters.Add<ConsentForms>();
        });
        return services;
    }
}
