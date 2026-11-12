using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // JWT (người dùng / app / MCP). Thiết bị nhận lệnh qua MQTT push, không qua HTTP.
public class ControlController : ControllerBase
{
    // Firmware bỏ IM LẶNG lệnh dài hơn 1024 byte (backend-api.md mục 2) -> chặn từ server.
    private const int MaxControlPayloadBytes = 1024;

    private const int MaxTimerSeconds = 3600;
    private const int DefaultShowQrSeconds = 300;

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

    /// <summary>
    /// Gửi lệnh điều khiển xuống thiết bị (publish MQTT xmini/control, QoS 1, khoá PHẲNG). Body là một JSON object có
    /// thể GỘP nhiều khoá (backend-api.md mục 5.2):
    ///   {"pump": true} (1 liều) | {"light": true} | {"light_pwm": 180} | {"mode": "auto"|"manual"} | {"auto": true} |
    ///   {"message": "..."} + {"message_secs": 15} | {"config": {...}} | {"show_qr": true|giây} | {"stay_awake_s": 600}.
    /// Thiết bị thật: pump / light / light_pwm đưa thiết bị về MANUAL; chỉ mode:"auto" / auto:true trả lại AUTO.
    /// Server tự thêm "id" duy nhất (id client gửi bị bỏ qua) và trả lại trong commandId. Khoá lạ bị bỏ qua.
    /// </summary>
    [HttpPost("{deviceId}")]
    public async Task<IActionResult> SendControl(string deviceId, [FromBody] JsonElement body)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest("Body phải là một JSON object với các khoá phẳng (pump/light/light_pwm/mode/auto/message/config/show_qr/stay_awake_s).");

            var (flat, error) = BuildControlPayload(body, SimTopics.IsSimDevice(deviceId));
            if (error != null) return error;
            if (flat.Count == 0)
                return BadRequest("Không có khoá điều khiển hợp lệ. Dùng: pump, light, light_pwm, mode, auto, message, message_secs, config, show_qr, stay_awake_s.");

            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending control to device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Cấu hình hiện tại BE nghe được từ topic xmini/config (null nếu chưa nghe thấy). Có 'rejected' / 'rejectedCount'
    /// khi lệnh config gần nhất có khoá bị thiết bị từ chối.
    /// </summary>
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
    /// Đặt (một phần) cấu hình: publish {"config": {...}} xuống thiết bị. Body = object các khoá snake_case (chỉ gửi khoá
    /// muốn đổi; đổi một cặp ngưỡng thì gửi CẢ HAI khoá). Server kiểm kiểu + miền + ràng buộc cặp như firmware và trả 400
    /// nếu sai. Thiết bị lưu NVS rồi echo lại lên xmini/config (xem GET .../config sau vài giây).
    /// </summary>
    [HttpPut("{deviceId}/config")]
    public async Task<IActionResult> SetConfig(string deviceId, [FromBody] JsonElement body)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest("Body phải là JSON object gồm các khoá cấu hình (vd soil_on_pct, pump_dose_ms...).");

            var (config, errors) = DeviceConfigRules.Validate(body, SimTopics.IsSimDevice(deviceId));
            if (errors.Count > 0) return InvalidConfig(errors);
            if (config.Count == 0)
                return BadRequest("Không có khoá cấu hình nào để đổi. Muốn hỏi cấu hình hiện tại thì gọi POST .../config/refresh.");

            var flat = new Dictionary<string, object?> { ["config"] = config };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting device config for {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Yêu cầu thiết bị publish lại cấu hình hiện tại: gửi {"config":{}} (backend-api.md mục 5.8).</summary>
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
    // Thiết bị thật làm chủ auto: lệnh tưới/đèn đưa thiết bị về MANUAL (tự động hoá đứng yên), /auto trả lại AUTO.
    // Thiết bị ảo SIM_* giữ luật cũ: server gộp mode hiện tại vào lệnh nên tưới/đèn không đổi mode.

    /// <summary>
    /// Tưới: on=true chạy ĐÚNG 1 liều (pump_dose_ms, mặc định 2 s) rồi tự tắt; on=false dừng liều đang chạy.
    /// Thiết bị thật chuyển MANUAL (kể cả khi lệnh bơm bị bỏ qua vì liều trước chưa xong) — gọi /auto để trả lại.
    /// </summary>
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
    /// Điều khiển đèn. Ưu tiên 'pwm' (0–255; &gt; 0 bật ở độ sáng đó, 0 tắt) nếu có; ngược lại dùng 'on' để bật/tắt ở độ
    /// sáng hiện tại. Thiết bị thật chuyển MANUAL — gọi /auto để trả lại (về AUTO với light_auto=false thì đèn tắt).
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

    /// <summary>
    /// Trả thiết bị về AUTO (tự tưới theo liều / tự đèn theo ngưỡng). Cũng gỡ soil_fault + safe_mode và kết thúc đợt
    /// tưới đang dở; gửi cả khi đang AUTO vẫn có tác dụng.
    /// </summary>
    [HttpPost("{deviceId}/auto")]
    public async Task<IActionResult> ReturnToAuto(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var flat = new Dictionary<string, object?> { ["mode"] = ControlModeResolver.Auto };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error returning device {DeviceId} to auto", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Hiện QR trang cấu hình của thiết bị (đổi WiFi / broker tại chỗ — màn không cảm ứng nên đây là cách duy nhất khi
    /// mạng đang bình thường). Body tuỳ chọn {"seconds": N}: bỏ trống = 300 s, 0 = tắt QR, tối đa 3600. Không đổi mode.
    /// </summary>
    [HttpPost("{deviceId}/show-qr")]
    public async Task<IActionResult> ShowQr(string deviceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ShowQrRequest? request)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var seconds = Math.Clamp(request?.Seconds ?? DefaultShowQrSeconds, 0, MaxTimerSeconds);
            var flat = new Dictionary<string, object?> { ["show_qr"] = seconds };
            return await PublishAndLogAsync(deviceId, flat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting QR screen for device {DeviceId}", deviceId);
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
        // "id" duy nhất đứng đầu: firmware kiểm id trước mọi khoá khác và bỏ lệnh trùng (QoS 1 có thể giao lặp).
        var payload = new Dictionary<string, object?> { ["id"] = CommandId.Next() };
        foreach (var (key, value) in flat)
            payload[key] = value;

        // Thiết bị thật: tính mode SẼ có sau lệnh (không sửa payload). Thiết bị ảo: gộp mode hiện tại vào lệnh actuator.
        var currentMode = await _modeStore.GetAsync(deviceId);
        var expectedMode = ControlModeResolver.Resolve(deviceId, payload, currentMode);

        var json = JsonSerializer.Serialize(payload);
        if (Encoding.UTF8.GetByteCount(json) > MaxControlPayloadBytes)
            return BadRequest($"Lệnh dài quá {MaxControlPayloadBytes} byte (thiết bị sẽ bỏ im lặng). Tách thành nhiều lệnh nhỏ hơn.");

        var ok = await _mqttPublisher.PublishControlAsync(deviceId, payload);
        if (!ok)
            return StatusCode(503, "MQTT publisher chưa kết nối broker — không gửi được lệnh.");

        // Ghi mode dự kiến ngay để app thấy liền; với thiết bị thật, telemetry kế tiếp sẽ xác nhận / sửa lại.
        if (expectedMode != currentMode)
            await _modeStore.SetAsync(deviceId, expectedMode);

        await _mongoDbService.InsertControlCommandAsync(new ControlCommand
        {
            DeviceId = deviceId,
            Payload = payload,
            CreatedAt = DateTime.UtcNow,
        });

        _logger.LogInformation("Control published to {DeviceId}: {Payload}", deviceId, json);
        return Ok(new
        {
            message = "Đã gửi lệnh xuống xmini/control",
            deviceId,
            commandId = payload["id"],
            expectedMode,
            published = payload,
        });
    }

    /// <summary>Lọc/validate body điều khiển thành payload phẳng chỉ gồm khoá thiết bị hiểu.</summary>
    private (Dictionary<string, object?> Flat, IActionResult? Error) BuildControlPayload(JsonElement body, bool isSim)
    {
        var flat = new Dictionary<string, object?>();

        foreach (var prop in body.EnumerateObject())
        {
            switch (prop.Name)
            {
                case "pump":
                case "light":
                case "auto":
                    if (prop.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return (flat, BadRequest($"'{prop.Name}' phải là boolean."));
                    flat[prop.Name] = prop.Value.GetBoolean();
                    break;

                case "light_pwm":
                    if (prop.Value.ValueKind != JsonValueKind.Number)
                        return (flat, BadRequest("'light_pwm' phải là số 0–255."));
                    flat["light_pwm"] = ClampToInt(prop.Value.GetDouble(), 0, 255); // firmware cũng kẹp
                    break;

                case "message_secs":
                    if (prop.Value.ValueKind != JsonValueKind.Number)
                        return (flat, BadRequest("'message_secs' phải là số nguyên ≥ 0."));
                    flat["message_secs"] = ClampToInt(prop.Value.GetDouble(), 0, int.MaxValue);
                    break;

                case "mode":
                    // Firmware: "auto" -> AUTO, MỌI chuỗi khác (kể cả "AUTO") -> MANUAL. Chặn lỗi gõ từ server.
                    var mode = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : null;
                    if (mode is not (ControlModeResolver.Auto or ControlModeResolver.Manual))
                        return (flat, BadRequest("'mode' phải là chuỗi \"auto\" hoặc \"manual\" (chữ thường)."));
                    flat["mode"] = mode;
                    break;

                case "message":
                    if (prop.Value.ValueKind != JsonValueKind.String)
                        return (flat, BadRequest("'message' phải là chuỗi (\"\" để xoá)."));
                    var raw = prop.Value.GetString();
                    var text = DeviceText.ToScreenText(raw);
                    if (text.Length == 0 && !string.IsNullOrWhiteSpace(raw))
                        return (flat, BadRequest("'message' không còn chữ nào sau khi bỏ dấu / emoji (màn hình chỉ hiện ASCII)."));
                    flat["message"] = text;
                    break;

                case "config":
                    if (prop.Value.ValueKind != JsonValueKind.Object)
                        return (flat, BadRequest("'config' phải là object các khoá cấu hình (hoặc {} để yêu cầu gửi lại)."));
                    var (cfg, errors) = DeviceConfigRules.Validate(prop.Value, isSim);
                    if (errors.Count > 0) return (flat, InvalidConfig(errors));
                    flat["config"] = cfg; // cho phép {} (yêu cầu thiết bị gửi lại cấu hình)
                    break;

                case "show_qr":
                    if (prop.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        flat["show_qr"] = prop.Value.GetBoolean() ? DefaultShowQrSeconds : 0;
                    else if (prop.Value.ValueKind == JsonValueKind.Number)
                        flat["show_qr"] = ClampToInt(prop.Value.GetDouble(), 0, MaxTimerSeconds);
                    else
                        return (flat, BadRequest("'show_qr' phải là true/false hoặc số giây 0–3600."));
                    break;

                case "stay_awake_s":
                    if (prop.Value.ValueKind != JsonValueKind.Number)
                        return (flat, BadRequest("'stay_awake_s' phải là số giây 0–3600 (0 = huỷ)."));
                    flat["stay_awake_s"] = ClampToInt(prop.Value.GetDouble(), 0, MaxTimerSeconds);
                    break;

                default:
                    // "id" do server tự sinh; khoá lạ: bỏ qua (đúng hành vi firmware).
                    break;
            }
        }

        return (flat, null);
    }

    private BadRequestObjectResult InvalidConfig(List<string> errors)
        => BadRequest(new { error = "Cấu hình không hợp lệ — lệnh KHÔNG được gửi xuống thiết bị.", errors });

    private static int ClampToInt(double value, int min, int max)
        => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), min, max);
}

/// <summary>Body cho POST /api/control/{deviceId}/water. on=true = 1 liều, on=false = dừng.</summary>
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

/// <summary>Body (tuỳ chọn) cho POST /api/control/{deviceId}/show-qr. null = 300 s, 0 = tắt QR.</summary>
public class ShowQrRequest
{
    public int? Seconds { get; set; }
}
