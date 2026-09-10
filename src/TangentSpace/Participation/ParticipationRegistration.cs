using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using CookieAuthentication = Koan.Web.Auth.Extensions.AuthenticationExtensions;

namespace TangentSpace.Participation;

public static class ParticipationRegistration
{
    public static IServiceCollection AddParticipation(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ParticipationCredentials>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ParticipantAuthenticationHandler>(ParticipationConstants.Scheme, _ => { })
            .AddPolicyScheme(ParticipationConstants.RequestScheme, "Tangent request identity", options =>
            {
                options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey("Authorization")
                    ? ParticipationConstants.Scheme : CookieAuthentication.CookieScheme;
                options.ForwardChallenge = ParticipationConstants.Scheme;
                options.ForwardForbid = ParticipationConstants.Scheme;
            });
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = ParticipationConstants.RequestScheme;
            options.DefaultChallengeScheme = ParticipationConstants.RequestScheme;
            options.DefaultForbidScheme = ParticipationConstants.RequestScheme;
        });
        return services;
    }
}
