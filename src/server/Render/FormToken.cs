using Microsoft.AspNetCore.Antiforgery;

namespace Tangent.Render;

/// <summary>A form's antiforgery token: the field a form posts it in and its value. A resource that carries a form
/// carries one, so the page and an agent reading the JSON post the same field; the handler that takes the form
/// validates it with ASP.NET's antiforgery before anything else.</summary>
public sealed record FormToken(string Field, string Value)
{
    /// <summary>A token for this request, its cookie stored on the response.</summary>
    public static FormToken For(HttpContext http)
    {
        var tokens = http.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(http);
        return new FormToken(tokens.FormFieldName, tokens.RequestToken
            ?? throw new InvalidOperationException("Antiforgery issued no request token."));
    }
}
