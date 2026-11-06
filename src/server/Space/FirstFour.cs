using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileProviders;
using Tangent.Render;

namespace Tangent.Space;

/// <summary>The place's first four tokens as its pages draw them in Paper, the light theme, each a colour as
/// <c>#rrggbb</c>, read from the stylesheets the place serves: Tangent's components, which name them, then its palette,
/// which gives them their values, each in the blocks every page's <c>:root</c> takes under the accent it wears. Only
/// Tangent's palette has them here; under a base's own palette, or where a sheet states one of them as anything but a hex
/// colour or another token, the place has none, and nothing guesses.</summary>
public sealed partial record FirstFour(string Background, string Foreground, string Accent, string AccentForeground)
{
    /// <summary>Each served sheet's declarations of the tokens, by the selector of the rule that holds them, read once for
    /// as long as the sheet stays as it was built.</summary>
    private static readonly ConcurrentDictionary<(string Sheet, DateTimeOffset Built), Dictionary<string, Dictionary<string, string>>> Sheets = new();

    /// <summary>The four <paramref name="appearance"/> wears, from the stylesheets under <paramref name="root"/>, the web
    /// root; null where it has none.</summary>
    public static FirstFour? Of(Appearance appearance, IFileProvider root)
    {
        if (appearance.Accent is not { } accent) return null;
        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sheet in new[] { HtmlFormatter.Stylesheet, appearance.PaletteSheet })
        {
            var rules = Rules(root, sheet);
            foreach (var selector in new[] { ":root", ":root[data-accent=\"" + accent + "\"]" })
                if (rules.TryGetValue(selector, out var tokens))
                    foreach (var (name, value) in tokens) declared[name] = value;
        }

        var background = Colour(declared, "--ts-background");
        var foreground = Colour(declared, "--ts-foreground");
        var worn = Colour(declared, "--ts-accent");
        var onAccent = Colour(declared, "--ts-accent-foreground");
        return background is null || foreground is null || worn is null || onAccent is null ? null : new FirstFour(background, foreground, worn, onAccent);
    }

    /// <summary>A token's value, through the tokens it names, as <c>#rrggbb</c>; null for anything else.</summary>
    private static string? Colour(Dictionary<string, string> declared, string token)
    {
        var value = declared.GetValueOrDefault(token);
        for (var hops = 0; hops < 8 && value is not null && Named().Match(value) is { Success: true } named; hops++)
            value = declared.GetValueOrDefault(named.Groups[1].Value);
        if (value is null || Hex().Match(value) is not { Success: true } hex) return null;
        var digits = hex.Groups[1].Value.ToLowerInvariant();
        return "#" + (digits.Length == 3 ? string.Concat(digits.Select(digit => new string(digit, 2))) : digits);
    }

    private static Dictionary<string, Dictionary<string, string>> Rules(IFileProvider root, string sheet)
    {
        var file = root.GetFileInfo(sheet);
        if (!file.Exists) return [];
        return Sheets.GetOrAdd((file.PhysicalPath ?? sheet, file.LastModified), _ =>
        {
            using var reader = new StreamReader(file.CreateReadStream());
            var rules = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            Walk(Comment().Replace(reader.ReadToEnd(), ""), rules);
            return rules;
        });
    }

    /// <summary>The token declarations of each rule on <c>:root</c>, in the order the sheet states them, by its selector:
    /// through every <c>@layer</c> block and past every other at-rule, whose rules hold only under its condition.</summary>
    private static void Walk(string css, Dictionary<string, Dictionary<string, string>> rules)
    {
        var at = 0;
        while (at < css.Length && css.IndexOf('{', at) is var open and >= 0)
        {
            var close = open + 1;
            for (var depth = 1; depth > 0 && close < css.Length; close++)
                depth += css[close] switch { '{' => 1, '}' => -1, _ => 0 };
            var prelude = css[at..open];
            var selector = Spaces().Replace(prelude[(prelude.LastIndexOf(';') + 1)..].Trim(), " ");
            var body = css[(open + 1)..Math.Max(open + 1, close - 1)];
            if (selector.StartsWith("@layer", StringComparison.Ordinal)) Walk(body, rules);
            else if (selector.StartsWith(":root", StringComparison.Ordinal))
            {
                if (!rules.TryGetValue(selector, out var tokens)) rules[selector] = tokens = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Match declaration in Declaration().Matches(body)) tokens[declaration.Groups[1].Value] = declaration.Groups[2].Value.Trim();
            }

            at = close;
        }
    }

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Comment();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"(--ts-[\w-]+)\s*:\s*([^;{}]+)", RegexOptions.CultureInvariant)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"^var\(\s*(--ts-[\w-]+)\s*\)$", RegexOptions.CultureInvariant)]
    private static partial Regex Named();

    [GeneratedRegex("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex Hex();
}
