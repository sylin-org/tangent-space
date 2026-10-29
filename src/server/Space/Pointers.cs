namespace Tangent.Space;

/// <summary>One way the site points at its Tangent Space: what <see cref="Kind"/> of pointer it is, where it goes, in
/// one sentence whose code is set between backticks, and the text to place there, ready to copy.</summary>
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

    /// <summary>Where the proxy rule passes to when the server's own addresses are not known: the port Tangent listens
    /// on unless its operator names another.</summary>
    public const string FixedUpstream = "http://127.0.0.1:5220";

    /// <summary>The address a reverse proxy on the same machine reaches the server at: the first http address it listens
    /// on, a wildcard or loopback host written as 127.0.0.1 and the port kept; <see cref="FixedUpstream"/> when it
    /// listens on none it reports.</summary>
    public static string Upstream(IEnumerable<string> listening)
    {
        foreach (var address in listening)
        {
            if (!address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
            var authority = address["http://".Length..].Split('/', 2)[0];
            var colon = authority.LastIndexOf(':');
            if (colon < 0 || !int.TryParse(authority[(colon + 1)..], out var port)) continue;
            var host = authority[..colon];
            var local = host is "+" or "*" or "0.0.0.0" or "[::]" or "[::1]" or "localhost" || host.StartsWith("127.", StringComparison.Ordinal);
            return $"http://{(local ? "127.0.0.1" : host)}:{port}";
        }

        return FixedUpstream;
    }

    /// <summary>The pointers for <paramref name="baseUrl"/>; the proxy rule, where there is one, passes to
    /// <paramref name="upstream"/>.</summary>
    public static IReadOnlyList<Pointer> For(string baseUrl, string upstream = FixedUpstream)
    {
        var address = new Uri(baseUrl);
        var site = SiteHost(address.Host);
        List<Pointer> pointers =
        [
            new(Link, "In the `<head>` of your site’s pages.", $"<link rel=\"tangent\" href=\"{baseUrl}\">"),
            new(WellKnown, $"Served at `{address.Scheme}://{site}/.well-known/tangent` as plain text, this one line.", baseUrl),
            new(Txt, $"A TXT record named `_tangent.{site}` in your DNS.", $"_tangent.{site}. IN TXT \"{baseUrl}\"")
        ];
        if (address.AbsolutePath != "/") pointers.Add(ProxyRule(address, upstream));
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
    private static Pointer ProxyRule(Uri address, string upstream)
    {
        var encoded = address.AbsolutePath;
        var path = Uri.UnescapeDataString(encoded);
        if (path.Contains('$') || path.Any(char.IsControl))
            return new Pointer(Proxy,
                $"nginx cannot express the path `{path}` safely, since it reads `$` as a variable; pass `{path}/` unchanged to `{upstream}` on your reverse proxy by hand, with `X-Forwarded-Proto` and `X-Forwarded-For`, and list the proxy’s address under `Koan:Web:TrustedProxies`.",
                "");
        return new Pointer(Proxy,
            $"In the server block for `{address.Authority}` on your reverse proxy; then list the proxy’s address under `Koan:Web:TrustedProxies`.",
            $$"""
            location = {{Nginx(path)}} {
                return 308 {{Nginx(encoded + "/")}};
            }
            location {{Nginx(path + "/")}} {
                proxy_pass {{upstream}};
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
