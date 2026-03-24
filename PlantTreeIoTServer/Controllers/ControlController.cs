using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ControlController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<ControlController> _logger;

    public ControlController(MongoDbService mongoDbService, ILogger<ControlController> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    /// <summary>
    /// ESP32 lấy lệnh điều khiển đang chờ xử lý
    /// </summary>
    [HttpGet("commands/{deviceId}")]
    public async Task<IActionResult> GetPendingCommands(string deviceId)
    {
        try
        {
            var commands = await _mongoDbService.GetPendingCommandsAsync(deviceId);
            return Ok(commands);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending commands for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Gửi lệnh điều khiển đến device
    /// </summary>
    [HttpPost("commands")]
    public async Task<IActionResult> SendCommand([FromBody] ControlCommandRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId) || string.IsNullOrEmpty(request.Command))
            {
                return BadRequest("DeviceId and Command are required");
            }

            var command = new ControlCommand
            {
                DeviceId = request.DeviceId,
                Command = request.Command,
                Parameters = request.Parameters,
                Executed = false,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.InsertControlCommandAsync(command);

            _logger.LogInformation("Command sent to device {DeviceId}: {Command}", request.DeviceId, request.Command);

            return Ok(new { message = "Command sent successfully", commandId = command.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending command");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// ESP32 báo cáo lệnh đã được thực hiện
    /// </summary>
    [HttpPost("commands/{commandId}/executed")]
    public async Task<IActionResult> MarkCommandExecuted(string commandId)
    {
        try
        {
            await _mongoDbService.MarkCommandExecutedAsync(commandId);

            _logger.LogInformation("Command {CommandId} marked as executed", commandId);

            return Ok(new { message = "Command marked as executed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking command as executed: {CommandId}", commandId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Tưới nước tự động dựa trên độ ẩm đất
    /// </summary>
    [HttpPost("auto-water/{deviceId}")]
    public async Task<IActionResult> AutoWater(string deviceId, [FromQuery] double threshold = 30.0)
    {
        try
        {
            // Lấy dữ liệu cảm biến mới nhất
            var latestData = await _mongoDbService.GetLatestSensorDataAsync(deviceId);
            if (latestData?.SoilMoisture == null)
            {
                return BadRequest("No soil moisture data available for device");
            }

            if (latestData.SoilMoisture < threshold)
            {
                var command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "WATER_ON",
                    Parameters = new Dictionary<string, object>
                    {
                        { "duration", 5000 }, // 5 giây
                        { "reason", "auto_water" },
                        { "threshold", threshold },
                        { "current_moisture", latestData.SoilMoisture }
                    },
                    Executed = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _mongoDbService.InsertControlCommandAsync(command);

                _logger.LogInformation("Auto water command sent to device {DeviceId} (moisture: {Moisture}%)",
                    deviceId, latestData.SoilMoisture);

                return Ok(new
                {
                    message = "Auto water command sent",
                    currentMoisture = latestData.SoilMoisture,
                    threshold = threshold,
                    commandId = command.Id
                });
            }
            else
            {
                return Ok(new
                {
                    message = "Soil moisture is adequate, no watering needed",
                    currentMoisture = latestData.SoilMoisture,
                    threshold = threshold
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in auto water for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Bật/tắt đèn dựa trên mức độ sáng
    /// </summary>
    [HttpPost("auto-light/{deviceId}")]
    public async Task<IActionResult> AutoLight(string deviceId, [FromQuery] double threshold = 200.0)
    {
        try
        {
            var latestData = await _mongoDbService.GetLatestSensorDataAsync(deviceId);
            if (latestData?.LightLevel == null)
            {
                return BadRequest("No light level data available for device");
            }

            string command;
            if (latestData.LightLevel < threshold)
            {
                command = "LIGHT_ON";
            }
            else
            {
                command = "LIGHT_OFF";
            }

            var controlCommand = new ControlCommand
            {
                DeviceId = deviceId,
                Command = command,
                Parameters = new Dictionary<string, object>
                {
                    { "reason", "auto_light" },
                    { "threshold", threshold },
                    { "current_light", latestData.LightLevel }
                },
                Executed = false,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.InsertControlCommandAsync(controlCommand);

            _logger.LogInformation("Auto light command sent to device {DeviceId}: {Command} (light: {Light})",
                deviceId, command, latestData.LightLevel);

            return Ok(new
            {
                message = $"Auto light command sent: {command}",
                currentLight = latestData.LightLevel,
                threshold = threshold,
                commandId = controlCommand.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in auto light for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public class ControlCommandRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
}