using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tangent.Api;

namespace Tangent.Identity;

public static class ParticipationRegistration
{
    public static IServiceCollection AddParticipation(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ConsumerRegistrations>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRegistration, WebRegistration>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRegistration, BearerRegistration>());
        services.AddSingleton<ParticipationCredentials>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ParticipantAuthenticationHandler>(ParticipationConstants.Scheme, _ => { })
            .AddPolicyScheme(ParticipationConstants.RequestScheme, "Tangent request identity", options =>
            {
                options.ForwardDefaultSelector = context => context.RequestServices
                    .GetRequiredService<ConsumerRegistrations>().SelectAuthentication(context.Request);
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
