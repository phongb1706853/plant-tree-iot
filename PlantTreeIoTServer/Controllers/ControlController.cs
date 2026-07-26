using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // JWT (người dùng / app / MCP). Thiết bị nhận lệnh qua MQTT push, không qua HTTP.
public class ControlController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private readonly MqttPublisherService _mqttPublisher;
    private readonly DeviceModeStore _modeStore;
    private readonly ILogger<ControlController> _logger;

    public ControlController(MongoDbService mongoDbService, MqttPublisherService mqttPublisher, DeviceModeStore modeStore, ILogger<ControlController> logger)
    {
        _mongoDbService = mongoDbService;
        _mqttPublisher = mqttPublisher;
        _modeStore = modeStore;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    // 15 khoá ngưỡng hợp lệ trong nhóm "config" (mqtt-api.md mục 4.3).
    private static readonly HashSet<string> ConfigKeys = new()
    {
        "soil_on_pct", "soil_off_pct", "pump_max_run_s", "pump_cooldown_s",
        "lux_on", "lux_off", "light_auto_pwm", "batt_warn_pct", "batt_recover_pct",
        "soil_dry", "soil_wet", "batt_full_on_v", "batt_full_off_v", "batt_crit_v", "batt_crit_recover_v",
    };

    /// <summary>
    /// Gửi lệnh điều khiển xuống thiết bị (publish MQTT xmini/control dưới dạng khoá PHẲNG).
    /// Body là một JSON object có thể GỘP nhiều nhóm khoá (mqtt-api.md mục 4):
    ///   {"pump": true} | {"light": true} | {"light_pwm": 180} | {"mode": "auto"|"manual"} |
    ///   {"auto": true} | {"message": "..."} + {"message_secs": 15} | {"config": {...ngưỡng...}}.
    /// Mode do SERVER làm chủ: pump/light KHÔNG đổi mode (server tự gộp mode hiện tại vào lệnh để
    /// firmware không rớt manual); chỉ {"mode":...}/{"auto":...} tường minh mới đổi mode. Xem ControlModeResolver.
    /// Khoá lạ bị bỏ qua. Không hỗ trợ WATER_ON/LIGHT_ON/FAN_* (thiết bị không hiểu).
    /// </summary>
    [HttpPost("{deviceId}")]
    public async Task<IActionResult> SendControl(string deviceId, [FromBody] JsonElement body)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest("Body phải là một JSON object với các khoá phẳng (pump/light/light_pwm/mode/auto/message/config).");

            var (flat, error) = BuildControlPayload(body);
            if (error != null) return BadRequest(error);
            if (flat.Count == 0)
                return BadRequest("Không có khoá điều khiển hợp lệ. Dùng: pump, light, light_pwm, mode, auto, message, message_secs, config.");

            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending control to device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Cấu hình ngưỡng auto hiện tại BE nghe được từ topic xmini/config (có thể null nếu chưa nghe thấy).</summary>
    [HttpGet("{deviceId}/config")]
    public async Task<IActionResult> GetConfig(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var config = await _mongoDbService.GetDeviceConfigAsync(deviceId);
            if (config == null)
                return Ok(new { deviceId, config = (object?)null, note = "Chưa nghe được xmini/config. Gọi POST .../config/refresh để yêu cầu thiết bị gửi lại." });

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device config for {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Đặt (một phần) ngưỡng auto: publish {"config": {...}} xuống thiết bị. Thiết bị clamp về dải hợp lệ,
    /// lưu NVS, rồi echo lại lên xmini/config. Body = object các khoá snake_case (chỉ gửi khoá muốn đổi).
    /// </summary>
    [HttpPut("{deviceId}/config")]
    public async Task<IActionResult> SetConfig(string deviceId, [FromBody] JsonElement body)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest("Body phải là JSON object gồm các khoá ngưỡng (vd soil_on_pct, lux_on...).");

            var (config, error) = ExtractConfigObject(body);
            if (error != null) return BadRequest(error);
            if (config.Count == 0)
                return BadRequest($"Không có khoá ngưỡng hợp lệ. Khoá cho phép: {string.Join(", ", ConfigKeys)}.");

            var flat = new Dictionary<string, object?> { ["config"] = config };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting device config for {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Yêu cầu thiết bị publish lại cấu hình hiện tại: gửi {"config":{}} (mqtt-api.md mục 4.3).</summary>
    [HttpPost("{deviceId}/config/refresh")]
    public async Task<IActionResult> RefreshConfig(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var flat = new Dictionary<string, object?> { ["config"] = new Dictionary<string, object?>() };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting config refresh for {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    // ===== Endpoint chuyên dụng cho App (bọc mỏng quanh flat-key xmini/control) =====
    // Mode do SERVER làm chủ (DeviceModeStore): lệnh tưới/đèn KHÔNG đổi mode — server gộp mode hiện
    // tại vào payload (ControlModeResolver) nên thiết bị giữ nguyên auto/manual. Chỉ /auto và lệnh
    // mode tường minh mới đổi mode. (Firmware vẫn ép manual khi thấy pump/light, nên server bù lại.)

    /// <summary>Tưới nước (bật/tắt bơm). KHÔNG đổi mode: đang auto vẫn auto, đang manual vẫn manual.</summary>
    [HttpPost("{deviceId}/water")]
    public async Task<IActionResult> Water(string deviceId, [FromBody] WaterRequest request)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var flat = new Dictionary<string, object?> { ["pump"] = request.On };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error controlling water for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Điều khiển đèn. Ưu tiên 'pwm' (0–255) nếu có; ngược lại dùng 'on' để bật/tắt.
    /// KHÔNG đổi mode: 'on' được server gộp kèm mode hiện tại; 'pwm' vốn không đổi mode ở firmware.
    /// </summary>
    [HttpPost("{deviceId}/light")]
    public async Task<IActionResult> Light(string deviceId, [FromBody] LightRequest request)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            Dictionary<string, object?> flat;
            if (request.Pwm.HasValue)
                flat = new() { ["light_pwm"] = Math.Clamp(request.Pwm.Value, 0, 255) };
            else if (request.On.HasValue)
                flat = new() { ["light"] = request.On.Value };
            else
                return BadRequest("Cần truyền 'on' (bool) hoặc 'pwm' (0–255).");

            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error controlling light for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Trả thiết bị về chế độ AUTO (thiết bị tự tưới/đèn theo ngưỡng).</summary>
    [HttpPost("{deviceId}/auto")]
    public async Task<IActionResult> ReturnToAuto(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var flat = new Dictionary<string, object?> { ["mode"] = "auto" };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error returning device {DeviceId} to auto", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Nhật ký các lệnh BE đã publish xuống thiết bị (mới nhất trước).</summary>
    [HttpGet("commands/{deviceId}")]
    public async Task<IActionResult> GetRecentCommands(string deviceId, [FromQuery] int limit = 50)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var commands = await _mongoDbService.GetRecentControlCommandsAsync(deviceId, limit);
            return Ok(commands);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting control command log for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    // ---------------------------------------------------------------------------------
    private async Task<IActionResult> PublishAndLogAsync(string deviceId, Dictionary<string, object?> flat)
    {
        // SERVER làm chủ mode: gộp mode hiện tại vào lệnh actuator (pump/light) để KHÔNG rớt auto;
        // chỉ lệnh mode/auto tường minh mới đổi mode. Xem ControlModeResolver. (Apply SỬA flat: thêm mode.)
        var newMode = ControlModeResolver.Apply(flat, await _modeStore.GetAsync(deviceId));

        var ok = await _mqttPublisher.PublishControlAsync(deviceId, flat);
        if (!ok)
            return StatusCode(503, "MQTT publisher chưa kết nối broker — không gửi được lệnh.");

        // Chỉ ghi nhận mode intent (bền qua redeploy) khi đã gửi được lệnh xuống broker.
        await _modeStore.SetAsync(deviceId, newMode);

        await _mongoDbService.InsertControlCommandAsync(new ControlCommand
        {
            DeviceId = deviceId,
            Payload = flat,
            CreatedAt = DateTime.UtcNow,
        });

        _logger.LogInformation("Control published to {DeviceId}: {Payload}", deviceId, JsonSerializer.Serialize(flat));
        return Ok(new { message = "Đã gửi lệnh xuống xmini/control", deviceId, published = flat });
    }

    /// <summary>Lọc/validate body điều khiển thành payload phẳng chỉ gồm khoá thiết bị hiểu.</summary>
    private (Dictionary<string, object?> Flat, string? Error) BuildControlPayload(JsonElement body)
    {
        var flat = new Dictionary<string, object?>();

        foreach (var prop in body.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "pump":
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return (flat, "'pump' phải là boolean.");
                    flat["pump"] = prop.Value.GetBoolean();
                    break;

                case "light":
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return (flat, "'light' phải là boolean.");
                    flat["light"] = prop.Value.GetBoolean();
                    break;

                case "auto":
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return (flat, "'auto' phải là boolean.");
                    flat["auto"] = prop.Value.GetBoolean();
                    break;

                case "light_pwm":
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var pwm))
                        return (flat, "'light_pwm' phải là số nguyên 0–255.");
                    flat["light_pwm"] = Math.Clamp(pwm, 0, 255);
                    break;

                case "message_secs":
                    if (prop.Value.ValueKind != JsonValueKind.Number || !prop.Value.TryGetInt32(out var secs))
                        return (flat, "'message_secs' phải là số nguyên ≥ 0.");
                    flat["message_secs"] = Math.Max(0, secs);
                    break;

                case "mode":
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        return (flat, "'mode' phải là chuỗi \"auto\" hoặc \"manual\".");
                    flat["mode"] = prop.Value.GetString();
                    break;

                case "message":
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        return (flat, "'message' phải là chuỗi (\"\" để xoá).");
                    flat["message"] = prop.Value.GetString();
                    break;

                case "config":
                    if (prop.Value.ValueKind != JsonValueKind.Object)
                        return (flat, "'config' phải là object các khoá ngưỡng (hoặc {} để yêu cầu gửi lại).");
                    var (cfg, err) = ExtractConfigObject(prop.Value);
                    if (err != null) return (flat, err);
                    flat["config"] = cfg; // cho phép {} (yêu cầu thiết bị gửi lại cấu hình)
                    break;

                default:
                    // Khoá lạ: bỏ qua (đúng hành vi firmware).
                    break;
            }
        }

        return (flat, null);
    }

    /// <summary>Lọc object "config" chỉ giữ 15 khoá ngưỡng hợp lệ; giữ nguyên kiểu số.</summary>
    private (Dictionary<string, object?> Config, string? Error) ExtractConfigObject(JsonElement configEl)
    {
        var cfg = new Dictionary<string, object?>();
        foreach (var prop in configEl.EnumerateObject())
        {
            if (!ConfigKeys.Contains(prop.Name)) continue; // khoá lạ -> bỏ qua
            if (prop.Value.ValueKind != JsonValueKind.Number)
                return (cfg, $"Ngưỡng '{prop.Name}' phải là số.");

            // int nếu là số nguyên, ngược lại double — giữ đúng kiểu để serialize sạch.
            if (prop.Value.TryGetInt32(out var i))
                cfg[prop.Name] = i;
            else
                cfg[prop.Name] = prop.Value.GetDouble();
        }
        return (cfg, null);
    }
}

/// <summary>Body cho POST /api/control/{deviceId}/water.</summary>
public class WaterRequest
{
    public bool On { get; set; }
}

/// <summary>Body cho POST /api/control/{deviceId}/light. 'pwm' ưu tiên nếu có; ngược lại dùng 'on'.</summary>
public class LightRequest
{
    public bool? On { get; set; }
    public int? Pwm { get; set; }
}
