# Tests

`Tangent.Tests` is the one test project. Its fixture, `TangentSpaceFixture`, starts the real application — `AddKoan()` over the server's `appsettings.json`, so `TangentModule`, its options and its pipeline contributors run as they do in production — on Kestrel at a free loopback port, with a new temp folder as its content root. Every test starts its own Tangent Space; none shares a port, a folder or a database, and the tests run in parallel. `rules/` holds the Node checks of the standing rules.

## Run

The pinned Koan checkout must be at `.local/upstream/koan-framework`; `pwsh scripts/prepare-framework.ps1` puts it there or verifies it.

```powershell
dotnet test tests/Tangent.Tests
node --test tests/rules
pwsh scripts/check-rules.ps1
```

## Add a test

Tests are grouped by noun, in folders that mirror `src/server` (`Space/` holds the tests of `src/server/Space`). A new noun gets a folder and a class; a new fact about an existing noun is a method in its class.

```csharp
[Fact]
public async Task Under_a_base_url_path_the_routes_answer_only_under_the_path()
{
    await using var tangentSpace = await TangentSpaceFixture.StartAsync(
        new Dictionary<string, string?> { ["Tangent:BaseUrl"] = "https://tangent.example/notes" },
        ct: TestContext.Current.CancellationToken);

    var response = await tangentSpace.Client.GetAsync("/notes/health/live", TestContext.Current.CancellationToken);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

- **Settings.** Pass any setting the test needs; it overrides `appsettings.json`. Leave `Tangent:DataFolder` relative so the data stays inside the fixture's folder.
- **Services.** The `services` argument adds registrations after the application's own, such as a hosted worker the test observes.
- **HTTP.** `Client` sends requests to the address the Tangent Space listens on.
- **Entities.** `StartAsync` binds Koan's ambient host to the new Tangent Space for the rest of the test method, and every request the Tangent Space serves runs bound to it, so `TangentSpace.Get(...)` and the other `Entity` statics read that Tangent Space's database. A test that starts two reads from the later one; it wraps reads of the earlier one in `using (AppHost.PushScope(earlier.Services))`.
- **Never** name a port, write outside `tangentSpace.Folder`, or switch off parallel test runs. The fixture deletes its folder when it is disposed, so `await using` is required.
