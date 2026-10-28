using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Koan.Data.Core;
using Microsoft.AspNetCore.DataProtection;
using SkiaSharp;
namespace Tangent.Media;

public sealed class MediaStorage(IWebHostEnvironment environment, IDataProtectionProvider protection)
{
    private readonly IDataProtector secrets = protection.CreateProtector("Tangent.MediaBucket.v1");
    public Task<MediaStorageSettings?> Settings(CancellationToken ct) => MediaStorageSettings.Get("media", ct);
    public MediaBucket Bucket(string endpoint, string bucket, string region, string accessKey, string secretKey)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Use an HTTPS bucket service address without a path.");
        if (string.IsNullOrWhiteSpace(bucket) || bucket.Length > 63 || bucket.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '.'))
            throw new ArgumentException("Enter a valid bucket name.");
        if (string.IsNullOrWhiteSpace(region) || region.Length > 64 || string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new ArgumentException("Enter the region and both bucket credentials.");
        return new MediaBucket { Id=Guid.CreateVersion7().ToString("n"), Endpoint=uri.AbsoluteUri, Bucket=bucket, Region=region,
            ProtectedAccessKey=secrets.Protect(accessKey), ProtectedSecretKey=secrets.Protect(secretKey) };
    }
    private AmazonS3Client Client(MediaBucket bucket) => new(new BasicAWSCredentials(secrets.Unprotect(bucket.ProtectedAccessKey),secrets.Unprotect(bucket.ProtectedSecretKey)),
        new AmazonS3Config { ServiceURL=bucket.Endpoint, AuthenticationRegion=bucket.Region, ForcePathStyle=true, Timeout=TimeSpan.FromSeconds(20) });
    public async Task Test(MediaBucket bucket, CancellationToken ct)
    {
        using var client=Client(bucket);
        var key="tangent-health/"+Guid.NewGuid().ToString("n");
        try {
            await client.PutObjectAsync(new PutObjectRequest { BucketName=bucket.Bucket,Key=key,ContentBody="Tangent storage connection test",DisablePayloadSigning=true },ct);
            using var result=await client.GetObjectAsync(bucket.Bucket,key,ct);
            using var reader=new StreamReader(result.ResponseStream);
            if(await reader.ReadToEndAsync(ct)!="Tangent storage connection test") throw new InvalidOperationException("The bucket returned different content.");
        } finally { await client.DeleteObjectAsync(bucket.Bucket,key,ct); }
    }
    private string Key(PostMediaAsset asset) => "tangent-media/"+asset.Id+asset.Extension;
    public string LocalPath(PostMediaAsset asset) => Path.Combine(environment.ContentRootPath,"media",asset.Id+asset.Extension);
    private async Task<MediaBucket> Configuration(PostMediaAsset asset,CancellationToken ct) => await MediaBucket.Get(asset.StoreId,ct) ?? throw new InvalidOperationException("The image storage connection is unavailable.");
    public async Task Write(PostMediaAsset asset,byte[] content,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(asset.StoreId)) { Directory.CreateDirectory(Path.GetDirectoryName(LocalPath(asset))!);await File.WriteAllBytesAsync(LocalPath(asset),content,ct);return; }
        var bucket=await Configuration(asset,ct);using var client=Client(bucket);using var stream=new MemoryStream(content,false);
        await client.PutObjectAsync(new PutObjectRequest { BucketName=bucket.Bucket,Key=Key(asset),InputStream=stream,ContentType=asset.ContentType,DisablePayloadSigning=true },ct);
    }
    public async Task<bool> Exists(PostMediaAsset asset,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(asset.StoreId))return File.Exists(LocalPath(asset));
        var bucket=await Configuration(asset,ct);using var client=Client(bucket);
        try { await client.GetObjectMetadataAsync(bucket.Bucket,Key(asset),ct);return true; }
        catch(AmazonS3Exception e) when(e.StatusCode==System.Net.HttpStatusCode.NotFound) { return false; }
    }
    public async Task<Stream> Open(PostMediaAsset asset,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(asset.StoreId))return File.OpenRead(LocalPath(asset));
        var bucket=await Configuration(asset,ct);using var client=Client(bucket);
        using var response=await client.GetObjectAsync(bucket.Bucket,Key(asset),ct);
        // Each image has a strict upload bound; the protected route never redirects to a public bucket URL.
        var stream=new MemoryStream();await response.ResponseStream.CopyToAsync(stream,ct);stream.Position=0;return stream;
    }
    public async Task Delete(PostMediaAsset asset,CancellationToken ct)
    {
        if(string.IsNullOrEmpty(asset.StoreId)) { if(File.Exists(LocalPath(asset)))File.Delete(LocalPath(asset));return; }
        var bucket=await Configuration(asset,ct);using var client=Client(bucket);await client.DeleteObjectAsync(bucket.Bucket,Key(asset),ct);
    }
    public static byte[] Process(byte[] input,MediaStorageSettings settings)
    {
        using var codec=SKCodec.Create(new SKMemoryStream(input));
        if(codec is null || codec.Info.Width<1 || codec.Info.Height<1 || (long)codec.Info.Width*codec.Info.Height>40_000_000)
            throw new ArgumentException("Choose a valid image with no more than 40 million pixels.");
        if(!settings.ResizeImages&&!settings.RemoveLocationData)return input;
        using var bitmap=SKBitmap.Decode(codec) ?? throw new ArgumentException("This image could not be read.");
        var rotated=codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var width=rotated?bitmap.Height:bitmap.Width;var height=rotated?bitmap.Width:bitmap.Height;
        var scale=settings.ResizeImages?Math.Min(1,2400d/Math.Max(width,height)):1;
        using var output=new SKBitmap(Math.Max(1,(int)(width*scale)),Math.Max(1,(int)(height*scale)));
        using(var canvas=new SKCanvas(output)) {
            canvas.Scale((float)scale);
            switch(codec.EncodedOrigin) {
                case SKEncodedOrigin.TopRight:canvas.Translate(width,0);canvas.Scale(-1,1);break;
                case SKEncodedOrigin.BottomRight:canvas.Translate(width,height);canvas.RotateDegrees(180);break;
                case SKEncodedOrigin.BottomLeft:canvas.Translate(0,height);canvas.Scale(1,-1);break;
                case SKEncodedOrigin.LeftTop:canvas.RotateDegrees(90);canvas.Scale(1,-1);break;
                case SKEncodedOrigin.RightTop:canvas.Translate(width,0);canvas.RotateDegrees(90);break;
                case SKEncodedOrigin.RightBottom:canvas.Translate(width,height);canvas.RotateDegrees(90);canvas.Scale(-1,1);break;
                case SKEncodedOrigin.LeftBottom:canvas.Translate(0,height);canvas.RotateDegrees(270);break;
            }
            using var paint=new SKPaint { IsAntialias=true };canvas.DrawBitmap(bitmap,0,0,paint);
        }
        using var image=SKImage.FromBitmap(output);using var encoded=image.Encode(SKEncodedImageFormat.Webp,90);
        return encoded.ToArray();
    }
}
