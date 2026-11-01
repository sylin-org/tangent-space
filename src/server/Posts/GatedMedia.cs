using Koan.Media.Abstractions.Recipes;
using Koan.Media.Web.Routing;

namespace Tangent.Posts;

/// <summary>What Koan's media routes serve: <see cref="Media"/>, to a reader it reaches (<see cref="MediaUse.Reaches"/>).
/// To anyone else it does not exist, and the routes answer as they do for an Id nothing has. Koan opens this before it
/// looks for a render or answers that nothing changed, so a render or a validator never passes the gate.</summary>
public sealed class GatedMedia(IHttpContextAccessor http) : IMediaSource
{
    private readonly MediaEntitySource<Media> _source = new();

    public async Task<MediaSourceHandle?> OpenAsync(string id, CancellationToken ct = default)
        => await MediaUse.Reaches(http.HttpContext is { } context ? PostWrites.PersonId(context.User) : null, id, ct)
            ? await _source.OpenAsync(id, ct)
            : null;

    public Task<MediaDerivationHandle?> OpenDerivationAsync(string sourceId, string recipeFingerprint, CancellationToken ct = default)
        => _source.OpenDerivationAsync(sourceId, recipeFingerprint, ct);

    public Task TryStoreDerivationAsync(string sourceId, string recipeFingerprint, MediaOutput output, string? recipeName, string? recipeVersion,
        CancellationToken ct = default)
        => _source.TryStoreDerivationAsync(sourceId, recipeFingerprint, output, recipeName, recipeVersion, ct);
}
