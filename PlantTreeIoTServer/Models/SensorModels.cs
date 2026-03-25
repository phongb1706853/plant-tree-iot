using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PlantTreeIoTServer.Models;

[BsonIgnoreExtraElements]
public class SensorData
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [BsonElement("timestamp")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [BsonElement("temperature")]
    public double? Temperature { get; set; }

    [BsonElement("humidity")]
    public double? Humidity { get; set; }

    [BsonElement("soilMoisture")]
    public double? SoilMoisture { get; set; }

    [BsonElement("lightLevel")]
    public double? LightLevel { get; set; }

    [BsonElement("waterLevel")]
    public double? WaterLevel { get; set; }

    [BsonElement("phLevel")]
    public double? PhLevel { get; set; }

    [BsonElement("location")]
    public string? Location { get; set; }
}

[BsonIgnoreExtraElements]
public class Device
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("location")]
    public string? Location { get; set; }

    [BsonElement("plantType")]
    public string? PlantType { get; set; }

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("lastSeen")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastSeen { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class MoistureRule
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Tưới khi độ ẩm xuống dưới ngưỡng này (%)</summary>
    [BsonElement("minMoisture")]
    public double MinMoisture { get; set; } = 30.0;

    /// <summary>Dừng tưới khi độ ẩm vượt ngưỡng này (%)</summary>
    [BsonElement("maxMoisture")]
    public double MaxMoisture { get; set; } = 70.0;

    /// <summary>Thời gian tưới mỗi lần (ms)</summary>
    [BsonElement("waterDurationMs")]
    public int WaterDurationMs { get; set; } = 5000;

    [BsonElement("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Thời gian chờ tối thiểu giữa 2 lần tưới (phút)</summary>
    [BsonElement("cooldownMinutes")]
    public int CooldownMinutes { get; set; } = 30;

    [BsonElement("lastTriggeredAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastTriggeredAt { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class LightRule
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Bat den khi lightLevel xuong duoi nguong nay</summary>
    [BsonElement("minLight")]
    public double MinLight { get; set; } = 25.0;

    /// <summary>Tat den khi lightLevel vuot nguong nay</summary>
    [BsonElement("maxLight")]
    public double MaxLight { get; set; } = 60.0;

    [BsonElement("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    [BsonElement("cooldownMinutes")]
    public int CooldownMinutes { get; set; } = 10;

    [BsonElement("lastTriggeredAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastTriggeredAt { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

[BsonIgnoreExtraElements]
public class ControlCommand
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [BsonElement("command")]
    public string Command { get; set; } = string.Empty; // "WATER_ON", "WATER_OFF", "LIGHT_ON", "LIGHT_OFF", etc.

    [BsonElement("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }

    [BsonElement("executed")]
    public bool Executed { get; set; } = false;

    [BsonElement("executedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ExecutedAt { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}