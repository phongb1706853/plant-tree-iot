using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Auth;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // mặc định = Bearer (JWT); action ESP32 override sang DeviceKey
public class SensorDataController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<SensorDataController> _logger;

    public SensorDataController(MongoDbService mongoDbService, ILogger<SensorDataController> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// ESP32 gui du lieu cam bien len server. Server tu dong kiem tra rule do am va tra ve lenh dieu khien.
    /// </summary>
    [Authorize(AuthenticationSchemes = DeviceKeyAuthenticationHandler.SchemeName)]
    [HttpPost("upload")]
    public async Task<IActionResult> UploadSensorData([FromBody] SensorDataUploadRequest request)
    {
        try
        {
            // Thiết bị chỉ được upload cho chính nó — ép deviceId theo token, bỏ qua body
            var authDeviceId = User.FindFirstValue("deviceId");
            if (!string.IsNullOrEmpty(authDeviceId))
                request.DeviceId = authDeviceId;

            if (string.IsNullOrEmpty(request.DeviceId))
            {
                return BadRequest("DeviceId is required");
            }

            var sensorData = new SensorData
            {
                DeviceId = request.DeviceId,
                Timestamp = DateTime.UtcNow,
                Temperature = request.Temperature,
                Humidity = request.Humidity,
                SoilMoisture = request.SoilMoisture,
                LightLevel = request.LightLevel,
                WaterLevel = request.WaterLevel,
                PhLevel = request.PhLevel,
                Pressure = request.Pressure,
                Altitude = request.Altitude,
                SoilMoistureRaw = request.SoilMoistureRaw,
                RelayOn = request.RelayOn,
                Location = request.Location
            };

            await _mongoDbService.InsertSensorDataAsync(sensorData);
            await _mongoDbService.UpdateDeviceLastSeenAsync(request.DeviceId);

            _logger.LogInformation("Sensor data uploaded from device {DeviceId}", request.DeviceId);

            // Evaluate rules and collect triggered commands
            var triggeredCommands = new List<object>();
            triggeredCommands.AddRange(await EvaluateMoistureRulesAsync(request.DeviceId, request.SoilMoisture));
            triggeredCommands.AddRange(await EvaluateLightRulesAsync(request.DeviceId, request.LightLevel));

            return Ok(new
            {
                message = "Data uploaded successfully",
                timestamp = sensorData.Timestamp,
                triggeredCommands
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading sensor data");
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<List<object>> EvaluateMoistureRulesAsync(string deviceId, double? soilMoisture)
    {
        var triggered = new List<object>();

        if (soilMoisture == null)
            return triggered;

        var rules = await _mongoDbService.GetMoistureRulesAsync(deviceId);
        var now = DateTime.UtcNow;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled)
                continue;

            // Check cooldown
            if (rule.LastTriggeredAt.HasValue &&
                (now - rule.LastTriggeredAt.Value).TotalMilliseconds < rule.CooldownMs)
                continue;

            ControlCommand? command = null;

            if (soilMoisture < rule.MinMoisture)
            {
                command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "WATER_ON",
                    Parameters = new Dictionary<string, object>
                    {
                        { "duration", rule.WaterDurationMs },
                        { "reason", "moisture_rule" },
                        { "ruleId", rule.Id! },
                        { "ruleName", rule.Name },
                        { "threshold", rule.MinMoisture },
                        { "currentMoisture", soilMoisture }
                    },
                    Executed = false,
                    CreatedAt = now
                };

                _logger.LogInformation(
                    "Moisture rule '{RuleName}' triggered WATER_ON for device {DeviceId} (moisture={Moisture}% < min={Min}%)",
                    rule.Name, deviceId, soilMoisture, rule.MinMoisture);
            }
            else if (soilMoisture >= rule.MaxMoisture)
            {
                command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "WATER_OFF",
                    Parameters = new Dictionary<string, object>
                    {
                        { "reason", "moisture_rule" },
                        { "ruleId", rule.Id! },
                        { "ruleName", rule.Name },
                        { "threshold", rule.MaxMoisture },
                        { "currentMoisture", soilMoisture }
                    },
                    Executed = false,
                    CreatedAt = now
                };

                _logger.LogInformation(
                    "Moisture rule '{RuleName}' triggered WATER_OFF for device {DeviceId} (moisture={Moisture}% >= max={Max}%)",
                    rule.Name, deviceId, soilMoisture, rule.MaxMoisture);
            }

            if (command != null)
            {
                await _mongoDbService.InsertControlCommandAsync(command);
                await _mongoDbService.UpdateRuleLastTriggeredAsync(rule.Id!);

                triggered.Add(new
                {
                    commandId = command.Id,
                    command = command.Command,
                    parameters = command.Parameters
                });
            }
        }

        return triggered;
    }

    private async Task<List<object>> EvaluateLightRulesAsync(string deviceId, double? lightLevel)
    {
        var triggered = new List<object>();
        if (lightLevel == null) return triggered;

        var rules = await _mongoDbService.GetLightRulesAsync(deviceId);
        var now = DateTime.UtcNow;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled) continue;
            if (rule.LastTriggeredAt.HasValue &&
                (now - rule.LastTriggeredAt.Value).TotalMilliseconds < rule.CooldownMs)
                continue;

            ControlCommand? command = null;

            if (lightLevel < rule.MinLight)
            {
                command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "LIGHT_ON",
                    Parameters = new Dictionary<string, object>
                    {
                        { "reason", "light_rule" },
                        { "ruleId", rule.Id! },
                        { "ruleName", rule.Name },
                        { "threshold", rule.MinLight },
                        { "currentLight", lightLevel }
                    },
                    Executed = false,
                    CreatedAt = now
                };
            }
            else if (lightLevel >= rule.MaxLight)
            {
                command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "LIGHT_OFF",
                    Parameters = new Dictionary<string, object>
                    {
                        { "reason", "light_rule" },
                        { "ruleId", rule.Id! },
                        { "ruleName", rule.Name },
                        { "threshold", rule.MaxLight },
                        { "currentLight", lightLevel }
                    },
                    Executed = false,
                    CreatedAt = now
                };
            }

            if (command != null)
            {
                await _mongoDbService.InsertControlCommandAsync(command);
                await _mongoDbService.UpdateLightRuleLastTriggeredAsync(rule.Id!);
                triggered.Add(new
                {
                    commandId = command.Id,
                    command = command.Command,
                    parameters = command.Parameters
                });
            }
        }

        return triggered;
    }

    /// <summary>
    /// Lấy dữ liệu cảm biến mới nhất của một device
    /// </summary>
    [HttpGet("latest/{deviceId}")]
    public async Task<IActionResult> GetLatestSensorData(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var data = await _mongoDbService.GetLatestSensorDataAsync(deviceId);
            if (data == null)
            {
                return NotFound($"No data found for device {deviceId}");
            }

            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest sensor data for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Lấy lịch sử dữ liệu cảm biến của một device
    /// </summary>
    [HttpGet("history/{deviceId}")]
    public async Task<IActionResult> GetSensorDataHistory(string deviceId, [FromQuery] int limit = 50)
    {
        try
        {
            if (await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var data = await _mongoDbService.GetSensorDataAsync(deviceId, limit);
            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sensor data history for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Lấy dữ liệu cảm biến trong khoảng thời gian
    /// </summary>
    [HttpGet("range/{deviceId}")]
    public async Task<IActionResult> GetSensorDataByDateRange(
        string deviceId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        try
        {
            if (await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            // Sử dụng service method thay vì truy cập trực tiếp database
            var allData = await _mongoDbService.GetSensorDataAsync(deviceId, 1000); // Lấy nhiều dữ liệu hơn
            var filteredData = allData
                .Where(d => d.Timestamp >= startDate.ToUniversalTime() &&
                           d.Timestamp <= endDate.ToUniversalTime())
                .OrderBy(d => d.Timestamp)
                .ToList();

            return Ok(filteredData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting sensor data by date range for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public class SensorDataUploadRequest
{
    [JsonPropertyName("deviceId")]
    [JsonInclude]
    public string DeviceId { get; set; } = string.Empty;

    [JsonPropertyName("device_id")]
    [JsonInclude]
    public string? DeviceIdSnake { set => DeviceId = value ?? DeviceId; get => null; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; set; }

    [JsonPropertyName("temperature_c")]
    public double? TemperatureC { set => Temperature = value ?? Temperature; get => null; }

    [JsonPropertyName("humidity")]
    public double? Humidity { get; set; }

    [JsonPropertyName("soilMoisture")]
    public double? SoilMoisture { get; set; }

    [JsonPropertyName("soil_moisture_percent")]
    public double? SoilMoisturePercent { set => SoilMoisture = value ?? SoilMoisture; get => null; }

    [JsonPropertyName("lightLevel")]
    public double? LightLevel { get; set; }

    [JsonPropertyName("light_lux")]
    public double? LightLux { set => LightLevel = value ?? LightLevel; get => null; }

    [JsonPropertyName("waterLevel")]
    public double? WaterLevel { get; set; }

    [JsonPropertyName("phLevel")]
    public double? PhLevel { get; set; }

    [JsonPropertyName("pressure")]
    public double? Pressure { get; set; }

    [JsonPropertyName("pressure_hpa")]
    public double? PressureHpa { set => Pressure = value ?? Pressure; get => null; }

    [JsonPropertyName("altitude")]
    public double? Altitude { get; set; }

    [JsonPropertyName("altitude_m")]
    public double? AltitudeM { set => Altitude = value ?? Altitude; get => null; }

    [JsonPropertyName("soilMoistureRaw")]
    public int? SoilMoistureRaw { get; set; }

    [JsonPropertyName("soil_moisture_raw")]
    public int? SoilMoistureRawSnake { set => SoilMoistureRaw = value ?? SoilMoistureRaw; get => null; }

    [JsonPropertyName("relayOn")]
    public bool? RelayOn { get; set; }

    [JsonPropertyName("relay_on")]
    public bool? RelayOnSnake { set => RelayOn = value ?? RelayOn; get => null; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }
}