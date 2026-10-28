using System.Security.Cryptography;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Tangent.Community;
using Tangent.Conversation;

namespace Tangent.Media;

/// <summary>Post media uses the Post's live audience; no file is published just by uploading it.</summary>
public sealed class PostMediaService(TopicGovernance topics, TimeProvider clock, MediaStorage storage) : IDisposable
{
    public const int MaximumBytes = 20 * 1024 * 1024;
    public const int MaximumReferences = 20;
    public const int MaximumDraftUploads = 40;
    private readonly SemaphoreSlim uploads = new(1, 1);

    public async Task<PostMediaAsset> Upload(string actorId, string tangentKey, string? topicKey,
        string? fileName, byte[] bytes, CancellationToken ct)
    {
        var settings = await storage.Settings(ct) ?? new MediaStorageSettings();
        if (bytes.Length == 0 || bytes.Length > settings.MaximumUploadMb * 1024 * 1024) throw new ArgumentException($"Choose an image no larger than {settings.MaximumUploadMb} MB.");
        ImageType(bytes);
        bytes = MediaStorage.Process(bytes, settings);
        if (bytes.Length > MaximumBytes) throw new ArgumentException("The processed image is too large.");
        var (extension, contentType) = ImageType(bytes);
        var asset = new PostMediaAsset
        {
            StoreId = settings.Location == "s3" ? settings.StoreId : "",
            Id = Guid.CreateVersion7().ToString("n"), OwnerParticipantId = actorId,
            TangentKey = tangentKey, TopicKey = topicKey, Extension = extension, ContentType = contentType,
            FileName = CleanName(fileName, extension), Size = bytes.Length, CreatedAt = clock.GetUtcNow(),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
        };
        await uploads.WaitAsync(ct);
        try
        {
            using var fresh = EntityContext.NoCache();
            var drafts = await PostMediaAsset.Query(row => row.OwnerParticipantId == actorId && row.PostId == null,
                new QueryDefinition { Page = 1, PageSize = MaximumDraftUploads }, ct);
            if (drafts.Count >= MaximumDraftUploads)
                throw new ArgumentException("This account has 40 unpublished images. Publish a draft before uploading more images.");
            try
            {
                await storage.Write(asset, bytes, ct);
                await asset.Save(ct);
            }
            catch
            {
                await storage.Delete(asset, CancellationToken.None);
                throw;
            }
            return asset;
        }
        finally { uploads.Release(); }
    }

    public async Task ValidateReferences(string actorId, string topicKey, string? postId,
        string text, string format, CancellationToken ct)
    {
        var ids = References(text, format);
        if (ids.Count == 0) return;
        using var fresh = EntityContext.NoCache();
        var topic = await Topic.Get(topicKey, ct) ?? throw new ArgumentException("Choose an existing Topic.");
        foreach (var id in ids)
        {
            var asset = await PostMediaAsset.Get(id, ct);
            if (asset is null || asset.OwnerParticipantId != actorId || asset.TangentKey != topic.TangentKey
                || asset.TopicKey is not null && asset.TopicKey != topicKey
                || asset.PostId is not null && asset.PostId != postId)
                throw new ArgumentException("Use an image you uploaded for this Post. Upload another copy to use it in another Post.");
            if (!await storage.Exists(asset, ct)) throw new ArgumentException("An attached image is unavailable. Upload it again.");
        }
    }

    /// <summary>Called within the Post writer gate, including matching retries after a partial commit.</summary>
    public async Task BindReferences(string actorId, string topicKey, string postId,
        string text, string format, CancellationToken ct)
    {
        await ValidateReferences(actorId, topicKey, postId, text, format, ct);
        foreach (var id in References(text, format))
        {
            var asset = (await PostMediaAsset.Get(id, ct))!;
            if (asset.PostId == postId) continue;
            asset.TopicKey = topicKey;
            asset.PostId = postId;
            await asset.Save(ct);
        }
    }

    public async Task<PostMediaAsset?> Read(string id, string? actorId, CancellationToken ct)
    {
        if (!ValidId(id)) return null;
        using var fresh = EntityContext.NoCache();
        var asset = await PostMediaAsset.Get(id, ct);
        if (asset is null || !await storage.Exists(asset, ct)) return null;
        if (asset.PostId is null) return asset.OwnerParticipantId == actorId ? asset : null;
        var post = await Post.Get(asset.PostId, ct);
        if (post is null || post.OfPostId is not null || post.TopicKey != asset.TopicKey) return null;
        if (!post.Removed && References(post.Content.Text, post.Content.Format).Contains(id))
        {
            var topic = await topics.Describe(actorId, post.TopicKey, ct);
            return topic?.CanRead == true ? asset : null;
        }
        // Removed images remain part of protected edit history, never anonymous media.
        return actorId is not null && await topics.CanReadHistory(actorId, post, ct) ? asset : null;
    }

    public Task<Stream> Open(PostMediaAsset asset, CancellationToken ct) => storage.Open(asset, ct);

    public string FilePath(PostMediaAsset asset)
    {
        if (!ValidId(asset.Id) || asset.Extension is not (".png" or ".jpg" or ".webp"))
            throw new InvalidDataException("Invalid media storage identity.");
        return storage.LocalPath(asset);
    }

    internal static IReadOnlySet<string> References(string text, string format)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (format != PostContent.Markdown) return ids;
        foreach (var link in Markdown.Parse(text).Descendants<LinkInline>())
        {
            var url = link.GetDynamicUrl is null ? link.Url : link.GetDynamicUrl();
            if (url is null || !url.StartsWith("/media/", StringComparison.Ordinal)) continue;
            var id = url[7..];
            if (!ValidId(id)) throw new ArgumentException("Use the original media URL returned by the image upload.");
            ids.Add(id);
            if (ids.Count > MaximumReferences) throw new ArgumentException("A Post may contain up to 20 uploaded images.");
        }
        return ids;
    }

    private static bool ValidId(string id)
        => id.Length == 32 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CleanName(string? fileName, string extension)
    {
        var name = new string((fileName ?? "image" + extension).Where(c => !char.IsControl(c) && c is not '/' and not '\\').Take(160).ToArray());
        return string.IsNullOrWhiteSpace(name) ? "image" + extension : name;
    }

    private static (string Extension, string ContentType) ImageType(byte[] bytes)
    {
        if (bytes.Length >= 24 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8)) return (".png", "image/png");
        if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return (".jpg", "image/jpeg");
        if (bytes.Length >= 16 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8)) return (".webp", "image/webp");
        throw new ArgumentException("Choose a PNG, JPEG, or WebP image.");
    }

    public void Dispose() => uploads.Dispose();
}
