using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Auth;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // mặc định = Bearer (JWT) cho người dùng
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

    private static string GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Đăng ký device mới. Owner = user đang đăng nhập. Trả về deviceSecret PLAINTEXT đúng 1 lần.
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

            var existingDevice = await _mongoDbService.GetDeviceAsync(request.DeviceId);
            if (existingDevice != null)
            {
                return Conflict($"Device {request.DeviceId} already exists");
            }

            var secret = GenerateSecret();
            var device = new Device
            {
                DeviceId = request.DeviceId,
                Name = request.Name,
                Location = request.Location,
                PlantType = request.PlantType,
                OwnerId = UserId,
                DeviceSecretHash = BCrypt.Net.BCrypt.HashPassword(secret),
                IsActive = true,
                LastSeen = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.CreateDeviceAsync(device);

            _logger.LogInformation("Device registered: {DeviceId} by user {UserId}", request.DeviceId, UserId);

            // deviceSecret chỉ hiển thị 1 lần — nạp vào firmware ESP32, server chỉ lưu hash
            return CreatedAtAction(nameof(GetDevice), new { deviceId = device.DeviceId },
                new { device.DeviceId, device.Name, deviceSecret = secret });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering device");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Lấy tất cả devices của user đang đăng nhập.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllDevices()
    {
        try
        {
            var devices = await _mongoDbService.GetDevicesByOwnerAsync(UserId);
            return Ok(devices);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting devices");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Lấy thông tin một device (chỉ khi user là chủ sở hữu).
    /// </summary>
    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
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
    /// Nhận sở hữu một device chưa có owner (dùng cho device cũ tạo trước khi có auth).
    /// Trả về device secret mới.
    /// </summary>
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
            var secret = GenerateSecret();
            await _mongoDbService.SetDeviceSecretAsync(deviceId, BCrypt.Net.BCrypt.HashPassword(secret));

            _logger.LogInformation("Device {DeviceId} claimed by user {UserId}", deviceId, UserId);
            return Ok(new { deviceId, deviceSecret = secret });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error claiming device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Sinh lại device secret (khi lộ hoặc quên). Chỉ owner mới làm được.
    /// </summary>
    [HttpPost("{deviceId}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(string deviceId)
    {
        try
        {
            var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
            if (device == null) return NotFound($"Device {deviceId} not found");

            var secret = GenerateSecret();
            await _mongoDbService.SetDeviceSecretAsync(deviceId, BCrypt.Net.BCrypt.HashPassword(secret));

            _logger.LogInformation("Device {DeviceId} secret rotated by user {UserId}", deviceId, UserId);
            return Ok(new { deviceId, deviceSecret = secret });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rotating secret for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// ESP32 heartbeat - cập nhật thời gian cuối cùng online. Xác thực bằng DeviceKey.
    /// </summary>
    [Authorize(AuthenticationSchemes = DeviceKeyAuthenticationHandler.SchemeName)]
    [HttpPost("{deviceId}/heartbeat")]
    public async Task<IActionResult> DeviceHeartbeat(string deviceId)
    {
        try
        {
            // Thiết bị chỉ được heartbeat cho chính nó
            var authDeviceId = User.FindFirstValue("deviceId");
            if (authDeviceId != deviceId)
                return Forbid();

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
