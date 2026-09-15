using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TangentSpace.Participation;

public static class ParticipationRegistration
{
    public static IServiceCollection AddParticipation(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<TangentSpace.Hosting.ConsumerRegistrations>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<TangentSpace.Hosting.IRegistration, TangentSpace.Web.WebRegistration>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<TangentSpace.Hosting.IRegistration, TangentSpace.Hosting.BearerRegistration>());
        services.AddSingleton<ParticipationCredentials>();
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ParticipantAuthenticationHandler>(ParticipationConstants.Scheme, _ => { })
            .AddPolicyScheme(ParticipationConstants.RequestScheme, "Tangent request identity", options =>
            {
                options.ForwardDefaultSelector = context => context.RequestServices
                    .GetRequiredService<TangentSpace.Hosting.ConsumerRegistrations>().SelectAuthentication(context.Request);
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
