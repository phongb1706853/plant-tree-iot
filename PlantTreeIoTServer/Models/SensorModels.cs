using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PlantTreeIoTServer.Models;

// =====================================================================================
// Telemetry lưu trong Mongo. Bám hợp đồng MQTT xmini/sensor_data của firmware 2.1.0 (30 trường phẳng).
// Trường lỗi cảm biến = null (battery_percent = -1 -> lưu null). Xem backend-api.md mục 4.1.
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

    // ----- Nguồn (INA219) -----
    /// <summary>power_source: "adapter" | "battery" (cố định theo bản build). Adapter -> mọi trường pin null / -1.</summary>
    [BsonElement("powerSource")]
    public string? PowerSource { get; set; }

    /// <summary>supply_voltage_v (V nguồn vào: adapter ~9 V hoặc pin). null khi INA219 lỗi hoặc đang chạy USB.</summary>
    [BsonElement("supplyVoltageV")]
    public double? SupplyVoltageV { get; set; }

    /// <summary>supply_current_ma (mA tổng dòng cả hệ).</summary>
    [BsonElement("supplyCurrentMa")]
    public double? SupplyCurrentMa { get; set; }

    /// <summary>power_state: "awake" | "eco" (trạng thái thực tế; bản adapter luôn "awake").</summary>
    [BsonElement("powerState")]
    public string? PowerState { get; set; }

    // ----- Pin (INA219, chỉ bản pin) -----
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

    // ----- Cờ chặn tự tưới + bộ đếm 24 h -----
    /// <summary>soil_ok (false = que đất hỏng/rút dây -> soil_percent vô nghĩa, chặn tự tưới).</summary>
    [BsonElement("soilOk")]
    public bool? SoilOk { get; set; }

    /// <summary>soil_fault (tưới đủ đợt mà đất không ẩm lên -> không mở đợt mới).</summary>
    [BsonElement("soilFault")]
    public bool? SoilFault { get; set; }

    /// <summary>safe_mode (≥ 3 lần reset bất thường liên tiếp -> chặn tự tưới).</summary>
    [BsonElement("safeMode")]
    public bool? SafeMode { get; set; }

    /// <summary>doses_today (số liều bơm trong 24 h cuốn chiếu, cả tay lẫn tự động).</summary>
    [BsonElement("dosesToday")]
    public int? DosesToday { get; set; }

    /// <summary>light_min_today (số phút đèn bật trong ~24 h; về 0 sau reboot).</summary>
    [BsonElement("lightMinToday")]
    public int? LightMinToday { get; set; }

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

    /// <summary>
    /// Chế độ auto/manual của thiết bị. Thiết bị THẬT (fw 2.1.0) tự chạy auto: giá trị này theo 'mode' trong
    /// telemetry (server chỉ ghi mode dự kiến ngay sau lệnh, telemetry kế tiếp xác nhận). Thiết bị ẢO SIM_*:
    /// server làm chủ (gộp vào lệnh actuator, gate auto-tưới). null = chưa đặt -> coi là "auto".
    /// Xem DeviceModeStore / ControlModeResolver.
    /// </summary>
    [BsonElement("controlMode")]
    public string? ControlMode { get; set; }

    /// <summary>
    /// Chỉ thiết bị ảo SIM_*: true = sự kiện thông báo gửi CẢ sang Notify thật (webhook), ngoài khung "🔔 Thông báo"
    /// trên dashboard. null/false = chỉ hiện ở khung (mặc định, không làm phiền người dùng thật). Xem NotifyDispatcher.
    /// </summary>
    [BsonElement("simNotifyForward")]
    public bool? SimNotifyForward { get; set; }

    [BsonElement("lastSeen")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastSeen { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// =====================================================================================
// Cấu hình thiết bị: 25 khoá của firmware 2.1.0 (backend-api.md mục 6) + 2 khoá cũ chỉ thiết bị ảo SIM_* dùng.
// Thiết bị TỰ chạy auto theo các ngưỡng này (lưu trong NVS). BE:
//   - đọc: nghe topic xmini/config (retained; thiết bị publish khi nối lần đầu + sau MỖI lệnh có khoá config)
//   - đặt: publish {"config":{...}} xuống xmini/control. Giá trị sai bị TỪ CHỐI (không kẹp) và báo trong
//     'rejected' / 'rejected_count' của bản tin xmini/config kế tiếp.
// =====================================================================================
[BsonIgnoreExtraElements]
public class DeviceConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    // Tưới theo liều
    [BsonElement("soilOnPct")] public int? SoilOnPct { get; set; }
    [BsonElement("soilOffPct")] public int? SoilOffPct { get; set; }
    [BsonElement("pumpDoseMs")] public int? PumpDoseMs { get; set; }
    [BsonElement("soakS")] public int? SoakS { get; set; }
    [BsonElement("maxDosesPerCycle")] public int? MaxDosesPerCycle { get; set; }
    [BsonElement("maxDosesPerDay")] public int? MaxDosesPerDay { get; set; }
    [BsonElement("soilDry")] public int? SoilDry { get; set; }
    [BsonElement("soilWet")] public int? SoilWet { get; set; }

    // Đèn tự động
    [BsonElement("lightAuto")] public bool? LightAuto { get; set; }
    [BsonElement("luxOn")] public double? LuxOn { get; set; }
    [BsonElement("luxOff")] public double? LuxOff { get; set; }
    [BsonElement("lightAutoPwm")] public int? LightAutoPwm { get; set; }
    [BsonElement("lightMinBattPct")] public int? LightMinBattPct { get; set; }
    [BsonElement("lightMaxMinPerDay")] public int? LightMaxMinPerDay { get; set; }

    // Pin (chỉ có tác dụng ở bản pin)
    [BsonElement("battWarnPct")] public int? BattWarnPct { get; set; }
    [BsonElement("battRecoverPct")] public int? BattRecoverPct { get; set; }
    [BsonElement("battFullOnV")] public double? BattFullOnV { get; set; }
    [BsonElement("battFullOffV")] public double? BattFullOffV { get; set; }
    [BsonElement("battCritV")] public double? BattCritV { get; set; }
    [BsonElement("battCritRecoverV")] public double? BattCritRecoverV { get; set; }

    // Nguồn / màn hình
    /// <summary>"eco" | "always_on" | "auto".</summary>
    [BsonElement("powerMode")] public string? PowerMode { get; set; }
    [BsonElement("wakeIntervalS")] public int? WakeIntervalS { get; set; }
    [BsonElement("pluggedOnV")] public double? PluggedOnV { get; set; }
    [BsonElement("pluggedOffV")] public double? PluggedOffV { get; set; }
    [BsonElement("screenTimeoutS")] public int? ScreenTimeoutS { get; set; }

    // Khoá cũ (firmware 1.x) — CHỈ thiết bị ảo SIM_* còn dùng; firmware 2.x bỏ qua im lặng.
    [BsonElement("pumpMaxRunS")] public int? PumpMaxRunS { get; set; }
    [BsonElement("pumpCooldownS")] public int? PumpCooldownS { get; set; }

    /// <summary>Khoá bị thiết bị từ chối ở lệnh config gần nhất (chỉ lấy từ bản tin LIVE, không từ bản retained).</summary>
    [BsonElement("rejected")] public List<string>? Rejected { get; set; }

    /// <summary>Số khoá bị từ chối — thiết bị gửi thay cho 'rejected' khi mảng làm bản tin quá 1024 byte.</summary>
    [BsonElement("rejectedCount")] public int? RejectedCount { get; set; }

    /// <summary>Lúc nhận bản tin xmini/config live mang rejected/rejected_count (null = lần gần nhất không có từ chối).</summary>
    [BsonElement("rejectedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? RejectedAt { get; set; }

    /// <summary>Lần cuối BE nghe được cấu hình này từ topic xmini/config.</summary>
    [BsonElement("updatedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// =====================================================================================
// Trạng thái sức khoẻ + nguồn gần nhất của thiết bị, nghe từ xmini/status (retained, có LWT "offline").
// Một bản ghi / thiết bị (upsert theo deviceId). Thiết bị không gửi giờ: receivedAt là giờ SERVER nhận.
// =====================================================================================
[BsonIgnoreExtraElements]
public class DeviceStatus
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>"online" | "sleeping" | "offline" (offline chỉ có trong LWT do broker phát).</summary>
    [BsonElement("state")] public string State { get; set; } = string.Empty;

    [BsonElement("fw")] public string? Fw { get; set; }
    [BsonElement("powerSource")] public string? PowerSource { get; set; }
    [BsonElement("powerMode")] public string? PowerMode { get; set; }
    [BsonElement("powerState")] public string? PowerState { get; set; }
    /// <summary>Chỉ có khi power_mode = "auto".</summary>
    [BsonElement("pluggedGuess")] public bool? PluggedGuess { get; set; }
    /// <summary>Chỉ có khi state = "sleeping": số giây tới lần thức kế tiếp, tính từ lúc gửi.</summary>
    [BsonElement("nextWakeS")] public int? NextWakeS { get; set; }
    [BsonElement("uptimeS")] public long? UptimeS { get; set; }
    [BsonElement("bootCount")] public int? BootCount { get; set; }
    [BsonElement("resetReason")] public string? ResetReason { get; set; }
    [BsonElement("freeHeap")] public int? FreeHeap { get; set; }
    [BsonElement("minFreeHeap")] public int? MinFreeHeap { get; set; }
    [BsonElement("maxAlloc")] public int? MaxAlloc { get; set; }
    [BsonElement("rssi")] public int? Rssi { get; set; }
    [BsonElement("wifiFailCount")] public int? WifiFailCount { get; set; }
    [BsonElement("wakeMs")] public int? WakeMs { get; set; }
    [BsonElement("safeMode")] public bool? SafeMode { get; set; }

    /// <summary>true = bản tin retained nhận lúc server vừa subscribe (tuổi không rõ — không dùng để tính hạn chót).</summary>
    [BsonElement("retained")] public bool Retained { get; set; }

    [BsonElement("receivedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
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
    [JsonPropertyName("power_source")] public string? PowerSource { get; set; }
    [JsonPropertyName("supply_voltage_v")] public double? SupplyVoltageV { get; set; }
    [JsonPropertyName("supply_current_ma")] public double? SupplyCurrentMa { get; set; }
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
    [JsonPropertyName("soil_ok")] public bool? SoilOk { get; set; }
    [JsonPropertyName("soil_fault")] public bool? SoilFault { get; set; }
    [JsonPropertyName("safe_mode")] public bool? SafeMode { get; set; }
    [JsonPropertyName("doses_today")] public int? DosesToday { get; set; }
    [JsonPropertyName("light_min_today")] public int? LightMinToday { get; set; }
    [JsonPropertyName("power_state")] public string? PowerState { get; set; }

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
        PowerSource = PowerSource,
        SupplyVoltageV = SupplyVoltageV,
        SupplyCurrentMa = SupplyCurrentMa,
        PowerState = PowerState,
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
        SoilOk = SoilOk,
        SoilFault = SoilFault,
        SafeMode = SafeMode,
        DosesToday = DosesToday,
        LightMinToday = LightMinToday,
    };
}

/// <summary>
/// Envelope của topic xmini/config: {"config": {...}, "rejected": [...]} hoặc {"config": {...}, "rejected_count": N}.
/// rejected / rejected_count chỉ có khi lệnh config vừa xử lý có khoá bị từ chối.
/// </summary>
public class XminiConfigEnvelope
{
    [JsonPropertyName("config")] public XminiConfigPayload? Config { get; set; }
    [JsonPropertyName("rejected")] public List<string>? Rejected { get; set; }
    [JsonPropertyName("rejected_count")] public int? RejectedCount { get; set; }

    /// <summary>Có khoá bị từ chối ở bản tin này không (mảng tên khoá hoặc chỉ số đếm).</summary>
    [JsonIgnore]
    public bool HasRejections => Rejected is { Count: > 0 } || RejectedCount is > 0;

    /// <summary>
    /// Map sang DeviceConfig. <paramref name="live"/> = bản tin retain=0: chỉ khi đó rejected mới đúng là của lệnh
    /// vừa gửi, nên chỉ khi đó mới ghi (hoặc xoá) thông tin từ chối. Bản retained giữ nguyên thông tin cũ.
    /// </summary>
    public DeviceConfig ToDeviceConfig(string deviceId, bool live, DateTime nowUtc)
    {
        var config = Config!.ToDeviceConfig(deviceId, nowUtc);
        if (live && HasRejections)
        {
            config.Rejected = Rejected;
            config.RejectedCount = RejectedCount ?? Rejected?.Count;
            config.RejectedAt = nowUtc;
        }
        return config;
    }
}

public class XminiConfigPayload
{
    [JsonPropertyName("soil_on_pct")] public int? SoilOnPct { get; set; }
    [JsonPropertyName("soil_off_pct")] public int? SoilOffPct { get; set; }
    [JsonPropertyName("pump_dose_ms")] public int? PumpDoseMs { get; set; }
    [JsonPropertyName("soak_s")] public int? SoakS { get; set; }
    [JsonPropertyName("max_doses_per_cycle")] public int? MaxDosesPerCycle { get; set; }
    [JsonPropertyName("max_doses_per_day")] public int? MaxDosesPerDay { get; set; }
    [JsonPropertyName("soil_dry")] public int? SoilDry { get; set; }
    [JsonPropertyName("soil_wet")] public int? SoilWet { get; set; }
    [JsonPropertyName("light_auto")] public bool? LightAuto { get; set; }
    [JsonPropertyName("lux_on")] public double? LuxOn { get; set; }
    [JsonPropertyName("lux_off")] public double? LuxOff { get; set; }
    [JsonPropertyName("light_auto_pwm")] public int? LightAutoPwm { get; set; }
    [JsonPropertyName("light_min_batt_pct")] public int? LightMinBattPct { get; set; }
    [JsonPropertyName("light_max_min_per_day")] public int? LightMaxMinPerDay { get; set; }
    [JsonPropertyName("batt_warn_pct")] public int? BattWarnPct { get; set; }
    [JsonPropertyName("batt_recover_pct")] public int? BattRecoverPct { get; set; }
    [JsonPropertyName("batt_full_on_v")] public double? BattFullOnV { get; set; }
    [JsonPropertyName("batt_full_off_v")] public double? BattFullOffV { get; set; }
    [JsonPropertyName("batt_crit_v")] public double? BattCritV { get; set; }
    [JsonPropertyName("batt_crit_recover_v")] public double? BattCritRecoverV { get; set; }
    [JsonPropertyName("power_mode")] public string? PowerMode { get; set; }
    [JsonPropertyName("wake_interval_s")] public int? WakeIntervalS { get; set; }
    [JsonPropertyName("plugged_on_v")] public double? PluggedOnV { get; set; }
    [JsonPropertyName("plugged_off_v")] public double? PluggedOffV { get; set; }
    [JsonPropertyName("screen_timeout_s")] public int? ScreenTimeoutS { get; set; }

    // Khoá cũ — chỉ thiết bị ảo SIM_* gửi lên.
    [JsonPropertyName("pump_max_run_s")] public int? PumpMaxRunS { get; set; }
    [JsonPropertyName("pump_cooldown_s")] public int? PumpCooldownS { get; set; }

    public DeviceConfig ToDeviceConfig(string deviceId, DateTime nowUtc) => new()
    {
        DeviceId = deviceId,
        SoilOnPct = SoilOnPct,
        SoilOffPct = SoilOffPct,
        PumpDoseMs = PumpDoseMs,
        SoakS = SoakS,
        MaxDosesPerCycle = MaxDosesPerCycle,
        MaxDosesPerDay = MaxDosesPerDay,
        SoilDry = SoilDry,
        SoilWet = SoilWet,
        LightAuto = LightAuto,
        LuxOn = LuxOn,
        LuxOff = LuxOff,
        LightAutoPwm = LightAutoPwm,
        LightMinBattPct = LightMinBattPct,
        LightMaxMinPerDay = LightMaxMinPerDay,
        BattWarnPct = BattWarnPct,
        BattRecoverPct = BattRecoverPct,
        BattFullOnV = BattFullOnV,
        BattFullOffV = BattFullOffV,
        BattCritV = BattCritV,
        BattCritRecoverV = BattCritRecoverV,
        PowerMode = PowerMode,
        WakeIntervalS = WakeIntervalS,
        PluggedOnV = PluggedOnV,
        PluggedOffV = PluggedOffV,
        ScreenTimeoutS = ScreenTimeoutS,
        PumpMaxRunS = PumpMaxRunS,
        PumpCooldownS = PumpCooldownS,
        UpdatedAt = nowUtc,
    };
}

/// <summary>DTO parse xmini/status (snake_case). LWT chỉ có state + device_id; các trường khác null.</summary>
public class XminiStatus
{
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("device_id")] public string? DeviceId { get; set; }
    [JsonPropertyName("fw")] public string? Fw { get; set; }
    [JsonPropertyName("power_source")] public string? PowerSource { get; set; }
    [JsonPropertyName("power_mode")] public string? PowerMode { get; set; }
    [JsonPropertyName("power_state")] public string? PowerState { get; set; }
    [JsonPropertyName("plugged_guess")] public bool? PluggedGuess { get; set; }
    [JsonPropertyName("next_wake_s")] public int? NextWakeS { get; set; }
    [JsonPropertyName("uptime_s")] public long? UptimeS { get; set; }
    [JsonPropertyName("boot_count")] public int? BootCount { get; set; }
    [JsonPropertyName("reset_reason")] public string? ResetReason { get; set; }
    [JsonPropertyName("free_heap")] public int? FreeHeap { get; set; }
    [JsonPropertyName("min_free_heap")] public int? MinFreeHeap { get; set; }
    [JsonPropertyName("max_alloc")] public int? MaxAlloc { get; set; }
    [JsonPropertyName("rssi")] public int? Rssi { get; set; }
    [JsonPropertyName("wifi_fail_count")] public int? WifiFailCount { get; set; }
    [JsonPropertyName("wake_ms")] public int? WakeMs { get; set; }
    [JsonPropertyName("safe_mode")] public bool? SafeMode { get; set; }

    public DeviceStatus ToDeviceStatus(bool retained, DateTime nowUtc) => new()
    {
        DeviceId = DeviceId ?? string.Empty,
        State = State ?? string.Empty,
        Fw = Fw,
        PowerSource = PowerSource,
        PowerMode = PowerMode,
        PowerState = PowerState,
        PluggedGuess = PluggedGuess,
        NextWakeS = NextWakeS,
        UptimeS = UptimeS,
        BootCount = BootCount,
        ResetReason = ResetReason,
        FreeHeap = FreeHeap,
        MinFreeHeap = MinFreeHeap,
        MaxAlloc = MaxAlloc,
        Rssi = Rssi,
        WifiFailCount = WifiFailCount,
        WakeMs = WakeMs,
        SafeMode = SafeMode,
        Retained = retained,
        ReceivedAt = nowUtc,
    };
}
