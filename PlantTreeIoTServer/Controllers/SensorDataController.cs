using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // JWT (người dùng / app / MCP). Thiết bị thật gửi telemetry qua MQTT (xmini/sensor_data).
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
    /// Nạp telemetry qua HTTP (đường phụ; thiết bị thật dùng MQTT xmini/sensor_data). Body theo đúng
    /// hợp đồng snake_case của xmini/sensor_data (device_id, temperature_c, soil_percent, ...).
    /// Thiết bị TỰ chạy auto — server KHÔNG còn eval rule / sinh lệnh tưới ở đây.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> UploadSensorData([FromBody] XminiTelemetry request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId))
                return BadRequest("device_id is required");

            if (await _mongoDbService.GetAccessibleDeviceAsync(request.DeviceId, UserId) == null)
                return NotFound($"Device {request.DeviceId} not found");

            var sensorData = request.ToSensorData();
            await _mongoDbService.InsertSensorDataAsync(sensorData);
            await _mongoDbService.UpdateDeviceLastSeenAsync(request.DeviceId);

            _logger.LogInformation("Sensor data uploaded (HTTP) from device {DeviceId}", request.DeviceId);

            return Ok(new { message = "Data uploaded successfully", timestamp = sensorData.Timestamp });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading sensor data");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Lấy dữ liệu cảm biến mới nhất của một device.</summary>
    [HttpGet("latest/{deviceId}")]
    public async Task<IActionResult> GetLatestSensorData(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var data = await _mongoDbService.GetLatestSensorDataAsync(deviceId);
            if (data == null)
                return NotFound($"No data found for device {deviceId}");

            return Ok(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest sensor data for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Lấy lịch sử dữ liệu cảm biến của một device.</summary>
    [HttpGet("history/{deviceId}")]
    public async Task<IActionResult> GetSensorDataHistory(string deviceId, [FromQuery] int limit = 50)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
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

    /// <summary>Lấy dữ liệu cảm biến trong khoảng thời gian.</summary>
    [HttpGet("range/{deviceId}")]
    public async Task<IActionResult> GetSensorDataByDateRange(
        string deviceId,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

            var allData = await _mongoDbService.GetSensorDataAsync(deviceId, 1000);
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
