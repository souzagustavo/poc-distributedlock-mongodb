using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DistributedLockPoc.Api.Models;

/// <summary>
/// Represents a shared counter — the classic race-condition target in high-concurrency scenarios.
/// Multiple instances incrementing this without a distributed lock would produce incorrect results.
/// </summary>
public class Counter
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("value")]
    public long Value { get; set; }

    [BsonElement("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; }

    [BsonElement("updatedBy")]
    public string UpdatedBy { get; set; } = string.Empty;
}
