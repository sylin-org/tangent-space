namespace Tangent.Api;

/// <summary>Singleton routing over singleton registrations. Authentication handlers remain
/// request-scoped because ASP.NET handlers carry request state.</summary>
public sealed class ConsumerRegistrations(IEnumerable<IRegistration> registrations)
{
    private readonly IRegistration[] registrations = registrations.ToArray();

    public string SelectAuthentication(HttpRequest request)
    {
        IRegistration? selected = null;
        foreach (var registration in registrations)
        {
            if (!registration.Accepts(request)) continue;
            if (selected is not null)
                throw new InvalidOperationException("Consumer authentication registrations overlap.");
            selected = registration;
        }
        return selected?.AuthenticationScheme
            ?? throw new InvalidOperationException("No consumer authentication registration accepts this request.");
    }
}
