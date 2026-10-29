using System.Globalization;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Data.Core.Polymorphism;
using Koan.Web.Infrastructure;

namespace Tangent.Posts;

/// <summary>Snapshots in Koan's audit partition, <see cref="KoanWebConstants.Sets.Audit"/>, taken by a command: the
/// entity as it stood at one version, copied whole through Koan's entity serializer and keyed
/// <c>{id}#v{version}</c>, the key Koan's entity audit gives a snapshot. The copy is saved through
/// <c>Data.Upsert(model, partition)</c>, which inside the writer's transaction is tracked like every other save of the
/// command, so a snapshot commits in the one native transaction with the change it records, or not at all. One
/// version is snapshotted once: the command that snapshots it raises the version or ends the entity's changes.</summary>
public static class AuditPartition
{
    /// <summary>The partition's name, as Koan names it.</summary>
    public const string Name = KoanWebConstants.Sets.Audit;

    /// <summary>The snapshot key of an entity at a version.</summary>
    public static string Key(string id, long version) => id + "#v" + version.ToString(CultureInfo.InvariantCulture);

    /// <summary>Saves a copy of <paramref name="entity"/> as it stands now, the state of <paramref name="version"/>, into
    /// the partition. Later changes to <paramref name="entity"/> do not reach the copy.</summary>
    public static Task Snapshot<TEntity>(TEntity entity, long version, CancellationToken ct)
        where TEntity : Entity<TEntity>
    {
        var copy = (TEntity)EntityJsonSerialization.DeserializeDocument(EntityJsonSerialization.SerializeDocument(entity), typeof(TEntity));
        copy.Id = Key(entity.Id, version);
        return Data<TEntity, string>.Upsert(copy, Name, ct);
    }

    /// <summary>The snapshot of an entity at a version, or null when none was taken; one keyed read.</summary>
    public static Task<TEntity?> At<TEntity>(string id, long version, CancellationToken ct)
        where TEntity : Entity<TEntity>
        => Data<TEntity, string>.Get(Key(id, version), Name, ct);
}
