using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // JWT (người dùng)
public class DevicesController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(MongoDbService mongoDbService, ILogger<DevicesController> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>Đăng ký device mới. Owner = user đang đăng nhập.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId) || string.IsNullOrEmpty(request.Name))
                return BadRequest("DeviceId and Name are required");

            if (await _mongoDbService.GetDeviceAsync(request.DeviceId) != null)
                return Conflict($"Device {request.DeviceId} already exists");

            var device = new Device
            {
                DeviceId = request.DeviceId,
                Name = request.Name,
                Location = request.Location,
                PlantType = request.PlantType,
                OwnerId = UserId,
                IsActive = true,
                LastSeen = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            await _mongoDbService.CreateDeviceAsync(device);

            _logger.LogInformation("Device registered: {DeviceId} by user {UserId}", request.DeviceId, UserId);
            return CreatedAtAction(nameof(GetDevice), new { deviceId = device.DeviceId },
                new { device.DeviceId, device.Name });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Danh sách device user sở hữu HOẶC được chia sẻ.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAllDevices()
    {
        try
        {
            return Ok(await _mongoDbService.GetDevicesForUserAsync(UserId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting devices");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Xem 1 device (owner hoặc được chia sẻ).</summary>
    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId);
            if (device == null) return NotFound($"Device {deviceId} not found");
            return Ok(device);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Nhận sở hữu device chưa có owner (device cũ tạo trước khi có auth).</summary>
    [HttpPost("{deviceId}/claim")]
    public async Task<IActionResult> Claim(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetDeviceAsync(deviceId);
            if (device == null) return NotFound($"Device {deviceId} not found");
            if (!string.IsNullOrEmpty(device.OwnerId))
                return Conflict("Device đã có chủ sở hữu");

            await _mongoDbService.SetDeviceOwnerAsync(deviceId, UserId);
            _logger.LogInformation("Device {DeviceId} claimed by user {UserId}", deviceId, UserId);
            return Ok(new { deviceId, message = "Claimed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error claiming device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Chia sẻ device cho user khác (bằng email). Chỉ owner.</summary>
    [HttpPost("{deviceId}/share")]
    public async Task<IActionResult> ShareDevice(string deviceId, [FromBody] ShareRequest request)
    {
        try
        {
            var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
            if (device == null) return NotFound($"Device {deviceId} not found");

            var target = await _mongoDbService.GetUserByEmailAsync(request.Email ?? "");
            if (target == null) return NotFound($"User {request.Email} not found");
            if (target.Id == device.OwnerId) return BadRequest("User đã là chủ sở hữu");

            await _mongoDbService.AddDeviceMemberAsync(deviceId, target.Id!);
            _logger.LogInformation("Device {DeviceId} shared with {Email} by {UserId}", deviceId, target.Email, UserId);
            return Ok(new { message = $"Đã chia sẻ {deviceId} cho {target.Email}", deviceId, sharedWith = target.Email });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sharing device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Thu hồi chia sẻ (theo userId của member). Chỉ owner.</summary>
    [HttpDelete("{deviceId}/share/{memberId}")]
    public async Task<IActionResult> UnshareDevice(string deviceId, string memberId)
    {
        try
        {
            var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
            if (device == null) return NotFound($"Device {deviceId} not found");

            await _mongoDbService.RemoveDeviceMemberAsync(deviceId, memberId);
            _logger.LogInformation("Device {DeviceId} unshared from {MemberId} by {UserId}", deviceId, memberId, UserId);
            return Ok(new { message = "Đã thu hồi chia sẻ", deviceId, memberId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsharing device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Danh sách owner + members của device (owner hoặc member xem được).</summary>
    [HttpGet("{deviceId}/members")]
    public async Task<IActionResult> GetMembers(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId);
            if (device == null) return NotFound($"Device {deviceId} not found");

            object? owner = null;
            if (!string.IsNullOrEmpty(device.OwnerId))
            {
                var o = await _mongoDbService.GetUserByIdAsync(device.OwnerId);
                if (o != null) owner = new { o.Id, o.Email, o.DisplayName };
            }
            var members = new List<object>();
            foreach (var mid in device.Members)
            {
                var u = await _mongoDbService.GetUserByIdAsync(mid);
                if (u != null) members.Add(new { u.Id, u.Email, u.DisplayName });
            }
            return Ok(new { deviceId, owner, members });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting members for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Xoá device (kèm sensor/config/command log). Chỉ owner.</summary>
    [HttpDelete("{deviceId}")]
    public async Task<IActionResult> DeleteDevice(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
            if (device == null) return NotFound($"Device {deviceId} not found");

            var deleted = await _mongoDbService.DeleteDeviceAndDataAsync(deviceId);
            _logger.LogInformation("Device {DeviceId} deleted by user {UserId}", deviceId, UserId);
            return Ok(new
            {
                message = $"Device {deviceId} deleted",
                deviceId,
                deletedSensorData = deleted.SensorData,
                deletedDeviceConfigs = deleted.DeviceConfigs,
                deletedCommands = deleted.Commands
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Heartbeat cập nhật lastSeen (owner/member). Thiết bị thật cập nhật qua MQTT.</summary>
    [HttpPost("{deviceId}/heartbeat")]
    public async Task<IActionResult> DeviceHeartbeat(string deviceId)
    {
        try
        {
            if (await _mongoDbService.GetAccessibleDeviceAsync(deviceId, UserId) == null)
                return NotFound($"Device {deviceId} not found");

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

public class ShareRequest
{
    public string Email { get; set; } = string.Empty;
}
