using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DevicesController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(MongoDbService mongoDbService, ILogger<DevicesController> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    /// <summary>
    /// Đăng ký device mới
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId) || string.IsNullOrEmpty(request.Name))
            {
                return BadRequest("DeviceId and Name are required");
            }

            // Kiểm tra device đã tồn tại chưa
            var existingDevice = await _mongoDbService.GetDeviceAsync(request.DeviceId);
            if (existingDevice != null)
            {
                return Conflict($"Device {request.DeviceId} already exists");
            }

            var device = new Device
            {
                DeviceId = request.DeviceId,
                Name = request.Name,
                Location = request.Location,
                PlantType = request.PlantType,
                IsActive = true,
                LastSeen = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.CreateDeviceAsync(device);

            _logger.LogInformation("Device registered: {DeviceId}", request.DeviceId);

            return CreatedAtAction(nameof(GetDevice), new { deviceId = device.DeviceId }, device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Lấy thông tin tất cả devices
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllDevices()
    {
        try
        {
            var devices = await _mongoDbService.GetAllDevicesAsync();
            return Ok(devices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all devices");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Lấy thông tin một device cụ thể
    /// </summary>
    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetDeviceAsync(deviceId);
            if (device == null)
            {
                return NotFound($"Device {deviceId} not found");
            }

            return Ok(device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// ESP32 heartbeat - cập nhật thời gian cuối cùng online
    /// </summary>
    [HttpPost("{deviceId}/heartbeat")]
    public async Task<IActionResult> DeviceHeartbeat(string deviceId)
    {
        try
        {
            await _mongoDbService.UpdateDeviceLastSeenAsync(deviceId);
            return Ok(new { message = "Heartbeat received", timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public class DeviceRegistrationRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? PlantType { get; set; }
}