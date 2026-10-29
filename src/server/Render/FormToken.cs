using Microsoft.AspNetCore.Antiforgery;
using Tangent.Delegates;

namespace Tangent.Render;

/// <summary>A form's antiforgery token: the field a form posts it in and its value. A resource that carries a form
/// carries one, so the page and an agent reading the JSON post the same field; the handler that takes the form
/// answers to <see cref="Validate"/>, the one rule every form post answers to, before anything else it asks of the
/// form.</summary>
public sealed record FormToken(string Field, string Value)
{
    /// <summary>A token for this request, its cookie stored on the response.</summary>
    public static FormToken For(HttpContext http)
    {
        var tokens = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        return new FormToken(tokens.FormFieldName, tokens.RequestToken
            ?? throw new InvalidOperationException("Antiforgery issued no request token."));
    }

    /// <summary>The rule for every form post: a request authenticated by a bearer token carries no ambient credential
    /// and needs no token; any other must carry a valid one, through ASP.NET's antiforgery.</summary>
    public static async Task<bool> Validate(HttpContext http)
    {
        if (DelegateIdentity.Delegated(http)) return true;
        try
        {
            await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
