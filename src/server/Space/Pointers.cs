namespace Tangent.Space;

/// <summary>One way the site points at its Tangent Space: what <see cref="Kind"/> of pointer it is, where it goes, in
/// one sentence, and the text to place there, ready to copy.</summary>
public sealed record Pointer(string Kind, string Where, string Text);

/// <summary>The pointers a site places so people and agents find its Tangent Space: a link on its pages, a file at its
/// origin, a DNS record, any one of them enough; and, for a base URL with a path, the rule the site's reverse proxy
/// needs to pass that path on. Each carries the base URL as Koan holds it.</summary>
public static class Pointers
{
    public const string Link = "link";
    public const string WellKnown = "well-known";
    public const string Txt = "txt";
    public const string Proxy = "proxy";

    /// <summary>The port the proxy rule passes to: the one Tangent listens on unless its operator names another.</summary>
    private const string Upstream = "http://127.0.0.1:5220";

    public static IReadOnlyList<Pointer> For(string baseUrl)
    {
        var address = new Uri(baseUrl);
        var site = SiteHost(address.Host);
        List<Pointer> pointers =
        [
            new(Link, "In the <head> of your site’s pages.", $"<link rel=\"tangent\" href=\"{baseUrl}\">"),
            new(WellKnown, $"Served at {address.Scheme}://{site}/.well-known/tangent as plain text, this one line.", baseUrl),
            new(Txt, $"A TXT record named _tangent.{site} in your DNS.", $"_tangent.{site}. IN TXT \"{baseUrl}\"")
        ];
        if (address.AbsolutePath != "/") pointers.Add(ProxyRule(address));
        return pointers;
    }

    /// <summary>The host of the site the Tangent Space sits beside: its own host without a leading <c>tangent.</c>
    /// label where what remains is a domain or <c>localhost</c>, or the same host for an install under a path.</summary>
    private static string SiteHost(string host)
    {
        var rest = host.StartsWith("tangent.", StringComparison.Ordinal) ? host["tangent.".Length..] : "";
        return rest.Contains('.') || rest == "localhost" ? rest : host;
    }

    /// <summary>An nginx rule for the server block of the site's host: the path without its slash goes to the path
    /// with it, and the path is passed on unchanged with the scheme and the client's address, since the Tangent
    /// Space serves under its path itself and believes those headers only from the proxies it names. A location
    /// matches the decoded path, written as an nginx string; the redirect names the encoded path. nginx reads
    /// <c>$</c> in a redirect as a variable and has no escape for it, so a path holding one, or a control character,
    /// gets no rule, only the reason.</summary>
    private static Pointer ProxyRule(Uri address)
    {
        var encoded = address.AbsolutePath;
        var path = Uri.UnescapeDataString(encoded);
        if (path.Contains('$') || path.Any(char.IsControl))
            return new Pointer(Proxy,
                $"nginx cannot express the path {path} safely, since it reads $ as a variable; pass {path}/ unchanged to {Upstream} on your reverse proxy by hand, with X-Forwarded-Proto and X-Forwarded-For, and list the proxy’s address under Koan:Web:TrustedProxies.",
                "");
        return new Pointer(Proxy,
            $"In the server block for {address.Authority} on your reverse proxy; then list the proxy’s address under Koan:Web:TrustedProxies.",
            $$"""
            location = {{Nginx(path)}} {
                return 308 {{Nginx(encoded + "/")}};
            }
            location {{Nginx(path + "/")}} {
                proxy_pass {{Upstream}};
                proxy_set_header X-Forwarded-Proto $scheme;
                proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
            }
            """);
    }

    /// <summary>A value as nginx reads it: bare when it holds nothing nginx's syntax treats specially, otherwise in
    /// double quotes with <c>\</c> and <c>"</c> escaped.</summary>
    private static string Nginx(string value)
        => value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\\' or '\'' or ';' or '{' or '}' or '#')
            ? "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""
            : value;
}
