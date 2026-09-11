namespace TangentSpace.Hosting;

/// <summary>A long-lived consumer's authentication selection. The selected ASP.NET handler
/// verifies each request; this registration never stores the request, actor or credentials.</summary>
public interface IRegistration
{
    string Name { get; }
    string AuthenticationScheme { get; }
    bool Accepts(HttpRequest request);
}
