using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tangent.Writer;
using Xunit;

namespace Tangent.Space;

/// <summary>What a proof of the place's address starts from: a Tangent Space with no URL configured, which believes a
/// proxy on loopback as it does by default, so a request says which origin it arrived on through the proxy's forwarded
/// scheme and host; a request on an origin; and the writer settled, so whatever a request handed it has run.</summary>
public static class Reached
{
    /// <summary>No base URL configured, Koan's test connector on, and no profile job reaching the network; with
    /// <paramref name="more"/> besides.</summary>
    public static Dictionary<string, string?> Unconfigured(params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string?>
        {
            [TangentModule.BaseUrlKey] = "",
            ["Koan:Web:Auth:TestProvider:Enabled"] = "true",
            ["Tangent:Profile:BaseAddress"] = "http://127.0.0.1:1/",
            ["Tangent:Profile:Timeout"] = "00:00:01"
        };
        foreach (var (key, value) in more) settings[key] = value;
        return settings;
    }

    /// <summary>Sends <paramref name="times"/> requests that arrive on <paramref name="origin"/> through the loopback
    /// proxy, and answers with the last one's body.</summary>
    public static async Task<string> On(TangentSpaceFixture tangentSpace, string origin, CancellationToken ct, int times = 1,
        string path = "/health/live", string accept = "application/json")
    {
        var uri = new Uri(origin);
        var body = "";
        for (var request = 0; request < times; request++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, path);
            message.Headers.Add("X-Forwarded-Proto", uri.Scheme);
            message.Headers.Add("X-Forwarded-Host", uri.Authority);
            message.Headers.Add("X-Forwarded-For", "203.0.113.9");
            message.Headers.Accept.ParseAdd(accept);
            using var response = await tangentSpace.Client.SendAsync(message, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            body = await response.Content.ReadAsStringAsync(ct);
        }

        return body;
    }

    /// <summary>Waits for the writer to run everything handed to it so far: one command of its own, behind them in its
    /// queue.</summary>
    public static async Task Settled(TangentSpaceFixture tangentSpace, CancellationToken ct)
        => Assert.IsType<Outcome<bool>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Settle(), ct));

    /// <summary>The place's row, set up by <paramref name="operatorId"/>, with <paramref name="address"/> as its address.</summary>
    public static async Task<TangentSpace> APlace(TangentSpaceFixture tangentSpace, string operatorId, string? address, CancellationToken ct)
    {
        var space = new TangentSpace { Name = "Lantern Works", ProprietorId = operatorId, OperatorId = operatorId, Address = address };
        Assert.IsType<Outcome<TangentSpace>.Done>(await tangentSpace.Services.GetRequiredService<WriteLoop>().Send(new Store<TangentSpace>(space), ct));
        return space;
    }

    private sealed record Settle : ICommand<bool>
    {
        public Task<Outcome<bool>> Run(Sequences sequences, CancellationToken ct) => Task.FromResult<Outcome<bool>>(new Outcome<bool>.Done(true));
    }
}
