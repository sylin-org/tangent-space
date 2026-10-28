using System.IO.Compression;
using System.Text.Json;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Sorting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Tangent.Api;
using Tangent.Infrastructure;
using Tangent.Application;
using Tangent.Identity;
using Tangent.Spaces;
namespace Tangent.Media;
[ApiController,Authorize,Route("api/media/storage")]
public sealed class MediaStorageController(MediaStorage media, PolicyGate gate) : ControllerBase
{
    private async Task Owner(CancellationToken ct) {
        var actor=ParticipationAccess.Require(User,ParticipationGrants.Manage);
        var space=await Space.Get(TangentConstants.SpaceId,ct);
        if(space?.IsOwner(actor)!=true || (await Participant.Get(actor,ct))?.IsSuspended!=false)throw new UnauthorizedAccessException();
        Response.Headers.CacheControl="no-store";
    }
    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct) {
        try {
            await Owner(ct);var settings=await media.Settings(ct)??new MediaStorageSettings {Id="media"};
            var bucket=string.IsNullOrEmpty(settings.StoreId)?null:await MediaBucket.Get(settings.StoreId,ct);
            return Ok(new {settings.Location,settings.MaximumUploadMb,settings.ResizeImages,settings.RemoveLocationData,endpoint=bucket?.Endpoint??"",bucket=bucket?.Bucket??"",region=bucket?.Region??"us-east-1",credentialsConfigured=bucket is not null});
        }catch(UnauthorizedAccessException){return Forbid();}
    }
    [HttpPatch,TopicMutation(ParticipationGrants.Manage)]
    public async Task<IActionResult> Save(MediaStoragePatch patch,CancellationToken ct) {
        var entered=false;
        try {
            using var fresh=EntityContext.NoCache();await Owner(ct);
            if(patch.Location is not("local" or "s3")||patch.MaximumUploadMb is <1 or >20)throw new ArgumentException("Choose This machine or S3, and a limit from 1 to 20 MB.");
            MediaBucket? config=null;
            if(patch.Location=="s3") {
                var previous=await media.Settings(ct);
                if(string.IsNullOrEmpty(patch.AccessKey)&&string.IsNullOrEmpty(patch.SecretKey)&&!string.IsNullOrEmpty(previous?.StoreId)) {
                    var stored=await MediaBucket.Get(previous.StoreId,ct);
                    if(stored?.Endpoint.TrimEnd('/')==patch.Endpoint.TrimEnd('/')&&stored.Bucket==patch.Bucket&&stored.Region==patch.Region)config=stored;
                }
                config??=media.Bucket(patch.Endpoint,patch.Bucket,patch.Region,patch.AccessKey,patch.SecretKey);
                await media.Test(config,ct);
            }
            await gate.Enter(ct);entered=true;await Owner(ct);
            var current=await media.Settings(ct)??new MediaStorageSettings {Id="media"};
            if(config is not null){await config.Save(ct);current.StoreId=config.Id;}
            current.Location=patch.Location;current.MaximumUploadMb=patch.MaximumUploadMb;current.ResizeImages=patch.ResizeImages;current.RemoveLocationData=patch.RemoveLocationData;
            await current.Save(ct);return Ok(new {saved=true});
        }catch(UnauthorizedAccessException){return Forbid();}
        catch(ArgumentException e){return BadRequest(new {message=e.Message});}
        catch(Exception e) when(e is Amazon.S3.AmazonS3Exception or HttpRequestException or IOException or InvalidOperationException){return BadRequest(new {message="The bucket could not complete a write, read and delete test. Check its address and credentials."});}
        finally{if(entered)gate.Exit();}
    }
    [HttpGet("download")]
    public async Task<IActionResult> Download(CancellationToken ct) {
        try {await Owner(ct);}catch(UnauthorizedAccessException){return Forbid();}
        var download=new FileStream(Path.Combine(Path.GetTempPath(),"tangent-media-"+Guid.NewGuid().ToString("N")+".zip"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.DeleteOnClose);
        try {
        var query=new QueryDefinition {Sort=SortSpecParser.ParseStrict<PostMediaAsset>("Id")};
        using(var archive=new ZipArchive(download,ZipArchiveMode.Create,true)) {
        for(var page=1;;page++) {
            var assets=await PostMediaAsset.Query(row=>true,query.WithPagination(page,50),ct);
            foreach(var asset in assets) {
                await using(var target=archive.CreateEntry("images/"+asset.Id+asset.Extension,CompressionLevel.NoCompression).Open())
                await using(var source=await media.Open(asset,ct))await source.CopyToAsync(target,ct);
                await using var manifest=archive.CreateEntry("metadata/"+asset.Id+".json").Open();
                await JsonSerializer.SerializeAsync(manifest,new {asset.Id,asset.OwnerParticipantId,asset.TangentKey,asset.TopicKey,asset.PostId,asset.FileName,asset.ContentType,asset.Size,asset.CreatedAt,asset.Sha256},cancellationToken:ct);
            }
            if(assets.Count<50)break;
        }
        }
        download.Position=0;return File(download,"application/zip","tangent-media.zip");
        }catch {await download.DisposeAsync();throw;}
    }
}
public sealed record MediaStoragePatch(string Location,int MaximumUploadMb,bool ResizeImages,bool RemoveLocationData,string Endpoint="",string Bucket="",string Region="us-east-1",string AccessKey="",string SecretKey="");
