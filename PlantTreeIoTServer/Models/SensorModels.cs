using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PlantTreeIoTServer.Models;

// =====================================================================================
// Telemetry lưu trong Mongo. Bám hợp đồng MQTT xmini/sensor_data (21 trường phẳng).
// Trường lỗi cảm biến = null (battery_percent = -1 -> lưu null). Xem mqtt-api.md mục 2.
// =====================================================================================
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

    // ----- Môi trường -----
    /// <summary>temperature_c (°C, AHT20).</summary>
    [BsonElement("temperature")]
    public double? Temperature { get; set; }

    /// <summary>humidity_percent (%RH, AHT20).</summary>
    [BsonElement("humidity")]
    public double? Humidity { get; set; }

    /// <summary>pressure_hpa (hPa, BMP280).</summary>
    [BsonElement("pressure")]
    public double? Pressure { get; set; }

    /// <summary>altitude_m (m, suy từ áp suất).</summary>
    [BsonElement("altitude")]
    public double? Altitude { get; set; }

    /// <summary>temperature_bmp_c (°C tham chiếu, BMP280).</summary>
    [BsonElement("temperatureBmp")]
    public double? TemperatureBmp { get; set; }

    /// <summary>light_lux (lux, BH1750).</summary>
    [BsonElement("lightLevel")]
    public double? LightLevel { get; set; }

    /// <summary>soil_percent (% 0–100, LM393). Hợp đồng: không bao giờ null.</summary>
    [BsonElement("soilPercent")]
    public int? SoilPercent { get; set; }

    /// <summary>soil_dry_flag (true = đất khô, chân DO).</summary>
    [BsonElement("soilDryFlag")]
    public bool? SoilDryFlag { get; set; }

    // ----- Pin (INA219) -----
    [BsonElement("batteryVoltageV")]
    public double? BatteryVoltageV { get; set; }

    [BsonElement("batteryCurrentMa")]
    public double? BatteryCurrentMa { get; set; }

    [BsonElement("batteryPowerMw")]
    public double? BatteryPowerMw { get; set; }

    /// <summary>battery_percent (% 0–100, đường cong OCV 2S). -1 khi lỗi -> lưu null.</summary>
    [BsonElement("batteryPercent")]
    public int? BatteryPercent { get; set; }

    // ----- Cơ cấu chấp hành + trạng thái -----
    [BsonElement("lightOn")]
    public bool? LightOn { get; set; }

    [BsonElement("lightPwm")]
    public int? LightPwm { get; set; }

    [BsonElement("pumpOn")]
    public bool? PumpOn { get; set; }

    /// <summary>"auto" | "manual".</summary>
    [BsonElement("mode")]
    public string? Mode { get; set; }

    /// <summary>water_ok (true = còn nước). null = tính năng cảm biến đang tắt.</summary>
    [BsonElement("waterOk")]
    public bool? WaterOk { get; set; }

    // ----- Cảnh báo / bảo vệ pin -----
    /// <summary>low_batt (theo %, batt_warn_pct).</summary>
    [BsonElement("lowBatt")]
    public bool? LowBatt { get; set; }

    /// <summary>batt_full (gần đầy theo điện áp).</summary>
    [BsonElement("battFull")]
    public bool? BattFull { get; set; }

    /// <summary>batt_cut (ĐANG cắt xả cạn cứng, điện áp ≤ batt_crit_v — cắt tải MỌI chế độ).</summary>
    [BsonElement("battCut")]
    public bool? BattCut { get; set; }

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

    /// <summary>Id của user sở hữu thiết bị (null = thiết bị cũ chưa được claim).</summary>
    [BsonElement("ownerId")]
    public string? OwnerId { get; set; }

    /// <summary>Danh sách userId được chia sẻ (xem + điều khiển). Owner KHÔNG nằm trong đây.</summary>
    [BsonElement("members")]
    public List<string> Members { get; set; } = new();

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("lastSeen")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastSeen { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// =====================================================================================
// Ngưỡng auto của thiết bị (15 tham số trong mqtt-api.md mục 3 / 4.3).
// Thiết bị TỰ chạy auto theo các ngưỡng này (lưu trong NVS). BE:
//   - đọc: nghe topic xmini/config (thiết bị publish khi kết nối + sau mỗi lần đổi)
//   - đặt: publish {"config":{...}} xuống xmini/control (thiết bị clamp + lưu NVS + echo lại)
// =====================================================================================
[BsonIgnoreExtraElements]
public class DeviceConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    [BsonElement("soilOnPct")] public int? SoilOnPct { get; set; }
    [BsonElement("soilOffPct")] public int? SoilOffPct { get; set; }
    [BsonElement("pumpMaxRunS")] public int? PumpMaxRunS { get; set; }
    [BsonElement("pumpCooldownS")] public int? PumpCooldownS { get; set; }
    [BsonElement("luxOn")] public double? LuxOn { get; set; }
    [BsonElement("luxOff")] public double? LuxOff { get; set; }
    [BsonElement("lightAutoPwm")] public int? LightAutoPwm { get; set; }
    [BsonElement("battWarnPct")] public int? BattWarnPct { get; set; }
    [BsonElement("battRecoverPct")] public int? BattRecoverPct { get; set; }
    [BsonElement("soilDry")] public int? SoilDry { get; set; }
    [BsonElement("soilWet")] public int? SoilWet { get; set; }
    [BsonElement("battFullOnV")] public double? BattFullOnV { get; set; }
    [BsonElement("battFullOffV")] public double? BattFullOffV { get; set; }
    [BsonElement("battCritV")] public double? BattCritV { get; set; }
    [BsonElement("battCritRecoverV")] public double? BattCritRecoverV { get; set; }

    /// <summary>Lần cuối BE nghe được cấu hình này từ topic xmini/config.</summary>
    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// =====================================================================================
// Nhật ký lệnh điều khiển BE đã publish xuống xmini/control (dùng để audit / hiển thị).
// Thiết bị THẬT nhận lệnh qua MQTT push (không polling HTTP).
// =====================================================================================
[BsonIgnoreExtraElements]
public class ControlCommand
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Payload phẳng đã gửi xuống xmini/control (vd {"pump":true} hoặc {"config":{...}}).</summary>
    [BsonElement("payload")]
    public Dictionary<string, object?> Payload { get; set; } = new();

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// =====================================================================================
// DTO parse telemetry đến từ thiết bị (snake_case, khớp 1-1 với xmini/sensor_data).
// =====================================================================================
public class XminiTelemetry
{
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("temperature_c")] public double? TemperatureC { get; set; }
    [JsonPropertyName("humidity_percent")] public double? HumidityPercent { get; set; }
    [JsonPropertyName("pressure_hpa")] public double? PressureHpa { get; set; }
    [JsonPropertyName("altitude_m")] public double? AltitudeM { get; set; }
    [JsonPropertyName("temperature_bmp_c")] public double? TemperatureBmpC { get; set; }
    [JsonPropertyName("light_lux")] public double? LightLux { get; set; }
    [JsonPropertyName("soil_percent")] public int? SoilPercent { get; set; }
    [JsonPropertyName("soil_dry_flag")] public bool? SoilDryFlag { get; set; }
    [JsonPropertyName("battery_voltage_v")] public double? BatteryVoltageV { get; set; }
    [JsonPropertyName("battery_current_ma")] public double? BatteryCurrentMa { get; set; }
    [JsonPropertyName("battery_power_mw")] public double? BatteryPowerMw { get; set; }
    [JsonPropertyName("battery_percent")] public int? BatteryPercent { get; set; }
    [JsonPropertyName("light_on")] public bool? LightOn { get; set; }
    [JsonPropertyName("light_pwm")] public int? LightPwm { get; set; }
    [JsonPropertyName("pump_on")] public bool? PumpOn { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("low_batt")] public bool? LowBatt { get; set; }
    [JsonPropertyName("batt_full")] public bool? BattFull { get; set; }
    [JsonPropertyName("batt_cut")] public bool? BattCut { get; set; }
    [JsonPropertyName("water_ok")] public bool? WaterOk { get; set; }

    public SensorData ToSensorData() => new()
    {
        DeviceId = DeviceId ?? string.Empty,
        Timestamp = DateTime.UtcNow,
        Temperature = TemperatureC,
        Humidity = HumidityPercent,
        Pressure = PressureHpa,
        Altitude = AltitudeM,
        TemperatureBmp = TemperatureBmpC,
        LightLevel = LightLux,
        SoilPercent = SoilPercent,
        SoilDryFlag = SoilDryFlag,
        BatteryVoltageV = BatteryVoltageV,
        BatteryCurrentMa = BatteryCurrentMa,
        BatteryPowerMw = BatteryPowerMw,
        // Quy ước lỗi: battery_percent = -1 nghĩa là INA219 lỗi -> coi như không có số.
        BatteryPercent = BatteryPercent == -1 ? null : BatteryPercent,
        LightOn = LightOn,
        LightPwm = LightPwm,
        PumpOn = PumpOn,
        Mode = Mode,
        WaterOk = WaterOk,
        LowBatt = LowBatt,
        BattFull = BattFull,
        BattCut = BattCut,
    };
}

/// <summary>Envelope của topic xmini/config: {"config": {...}}.</summary>
public class XminiConfigEnvelope
{
    [JsonPropertyName("config")] public XminiConfigPayload? Config { get; set; }
}

public class XminiConfigPayload
{
    [JsonPropertyName("soil_on_pct")] public int? SoilOnPct { get; set; }
    [JsonPropertyName("soil_off_pct")] public int? SoilOffPct { get; set; }
    [JsonPropertyName("pump_max_run_s")] public int? PumpMaxRunS { get; set; }
    [JsonPropertyName("pump_cooldown_s")] public int? PumpCooldownS { get; set; }
    [JsonPropertyName("lux_on")] public double? LuxOn { get; set; }
    [JsonPropertyName("lux_off")] public double? LuxOff { get; set; }
    [JsonPropertyName("light_auto_pwm")] public int? LightAutoPwm { get; set; }
    [JsonPropertyName("batt_warn_pct")] public int? BattWarnPct { get; set; }
    [JsonPropertyName("batt_recover_pct")] public int? BattRecoverPct { get; set; }
    [JsonPropertyName("soil_dry")] public int? SoilDry { get; set; }
    [JsonPropertyName("soil_wet")] public int? SoilWet { get; set; }
    [JsonPropertyName("batt_full_on_v")] public double? BattFullOnV { get; set; }
    [JsonPropertyName("batt_full_off_v")] public double? BattFullOffV { get; set; }
    [JsonPropertyName("batt_crit_v")] public double? BattCritV { get; set; }
    [JsonPropertyName("batt_crit_recover_v")] public double? BattCritRecoverV { get; set; }

    public DeviceConfig ToDeviceConfig(string deviceId) => new()
    {
        DeviceId = deviceId,
        SoilOnPct = SoilOnPct,
        SoilOffPct = SoilOffPct,
        PumpMaxRunS = PumpMaxRunS,
        PumpCooldownS = PumpCooldownS,
        LuxOn = LuxOn,
        LuxOff = LuxOff,
        LightAutoPwm = LightAutoPwm,
        BattWarnPct = BattWarnPct,
        BattRecoverPct = BattRecoverPct,
        SoilDry = SoilDry,
        SoilWet = SoilWet,
        BattFullOnV = BattFullOnV,
        BattFullOffV = BattFullOffV,
        BattCritV = BattCritV,
        BattCritRecoverV = BattCritRecoverV,
        UpdatedAt = DateTime.UtcNow,
    };
}
