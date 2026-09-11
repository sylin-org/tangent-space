using TangentSpace.Hosting;
using CookieAuthentication = Koan.Web.Auth.Extensions.AuthenticationExtensions;

namespace TangentSpace.Web;

public sealed class WebRegistration : IRegistration
{
    public string Name => "web";
    public string AuthenticationScheme => CookieAuthentication.CookieScheme;
    public bool Accepts(HttpRequest request) => !request.Headers.ContainsKey("Authorization");
}
