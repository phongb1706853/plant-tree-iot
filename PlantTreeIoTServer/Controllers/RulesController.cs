using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RulesController : ControllerBase
{
    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<RulesController> _logger;

    public RulesController(MongoDbService mongoDbService, ILogger<RulesController> logger)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
    }

    /// <summary>
    /// Lay danh sach rule do am cua mot device
    /// </summary>
    [HttpGet("moisture/{deviceId}")]
    public async Task<IActionResult> GetMoistureRules(string deviceId)
    {
        try
        {
            var rules = await _mongoDbService.GetMoistureRulesAsync(deviceId);
            return Ok(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting moisture rules for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Tao rule do am moi cho device
    /// </summary>
    [HttpPost("moisture")]
    public async Task<IActionResult> CreateMoistureRule([FromBody] MoistureRuleRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId))
                return BadRequest("DeviceId is required");

            if (request.MinMoisture >= request.MaxMoisture)
                return BadRequest("MinMoisture must be less than MaxMoisture");

            var rule = new MoistureRule
            {
                DeviceId = request.DeviceId,
                Name = request.Name,
                MinMoisture = request.MinMoisture,
                MaxMoisture = request.MaxMoisture,
                WaterDurationMs = request.WaterDurationMs,
                IsEnabled = request.IsEnabled,
                CooldownMinutes = request.CooldownMinutes,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.InsertMoistureRuleAsync(rule);

            _logger.LogInformation("Moisture rule created for device {DeviceId}: min={Min}%, max={Max}%",
                request.DeviceId, request.MinMoisture, request.MaxMoisture);

            return Created($"/api/rules/moisture/rule/{rule.Id}", rule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating moisture rule");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Cap nhat rule do am
    /// </summary>
    [HttpPut("moisture/{ruleId}")]
    public async Task<IActionResult> UpdateMoistureRule(string ruleId, [FromBody] MoistureRuleRequest request)
    {
        try
        {
            if (request.MinMoisture >= request.MaxMoisture)
                return BadRequest("MinMoisture must be less than MaxMoisture");

            var updated = new MoistureRule
            {
                Name = request.Name,
                MinMoisture = request.MinMoisture,
                MaxMoisture = request.MaxMoisture,
                WaterDurationMs = request.WaterDurationMs,
                IsEnabled = request.IsEnabled,
                CooldownMinutes = request.CooldownMinutes
            };

            var success = await _mongoDbService.UpdateMoistureRuleAsync(ruleId, updated);
            if (!success)
                return NotFound($"Rule {ruleId} not found");

            _logger.LogInformation("Moisture rule {RuleId} updated", ruleId);
            return Ok(new { message = "Rule updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating moisture rule {RuleId}", ruleId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Xoa rule do am
    /// </summary>
    [HttpDelete("moisture/{ruleId}")]
    public async Task<IActionResult> DeleteMoistureRule(string ruleId)
    {
        try
        {
            var success = await _mongoDbService.DeleteMoistureRuleAsync(ruleId);
            if (!success)
                return NotFound($"Rule {ruleId} not found");

            _logger.LogInformation("Moisture rule {RuleId} deleted", ruleId);
            return Ok(new { message = "Rule deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting moisture rule {RuleId}", ruleId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("light/{deviceId}")]
    public async Task<IActionResult> GetLightRules(string deviceId)
    {
        try
        {
            var rules = await _mongoDbService.GetLightRulesAsync(deviceId);
            return Ok(rules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting light rules for device {DeviceId}", deviceId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("light")]
    public async Task<IActionResult> CreateLightRule([FromBody] LightRuleRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.DeviceId))
                return BadRequest("DeviceId is required");

            if (request.MinLight >= request.MaxLight)
                return BadRequest("MinLight must be less than MaxLight");

            var rule = new LightRule
            {
                DeviceId = request.DeviceId,
                Name = request.Name,
                MinLight = request.MinLight,
                MaxLight = request.MaxLight,
                IsEnabled = request.IsEnabled,
                CooldownMinutes = request.CooldownMinutes,
                CreatedAt = DateTime.UtcNow
            };

            await _mongoDbService.InsertLightRuleAsync(rule);
            _logger.LogInformation("Light rule created for device {DeviceId}: min={Min}, max={Max}",
                request.DeviceId, request.MinLight, request.MaxLight);

            return Created($"/api/rules/light/rule/{rule.Id}", rule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating light rule");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("light/{ruleId}")]
    public async Task<IActionResult> UpdateLightRule(string ruleId, [FromBody] LightRuleRequest request)
    {
        try
        {
            if (request.MinLight >= request.MaxLight)
                return BadRequest("MinLight must be less than MaxLight");

            var updated = new LightRule
            {
                Name = request.Name,
                MinLight = request.MinLight,
                MaxLight = request.MaxLight,
                IsEnabled = request.IsEnabled,
                CooldownMinutes = request.CooldownMinutes
            };

            var success = await _mongoDbService.UpdateLightRuleAsync(ruleId, updated);
            if (!success) return NotFound($"Rule {ruleId} not found");

            return Ok(new { message = "Rule updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating light rule {RuleId}", ruleId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("light/{ruleId}")]
    public async Task<IActionResult> DeleteLightRule(string ruleId)
    {
        try
        {
            var success = await _mongoDbService.DeleteLightRuleAsync(ruleId);
            if (!success) return NotFound($"Rule {ruleId} not found");

            return Ok(new { message = "Rule deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting light rule {RuleId}", ruleId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public class MoistureRuleRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double MinMoisture { get; set; } = 30.0;
    public double MaxMoisture { get; set; } = 70.0;
    public int WaterDurationMs { get; set; } = 5000;
    public bool IsEnabled { get; set; } = true;
    public int CooldownMinutes { get; set; } = 30;
}

public class LightRuleRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double MinLight { get; set; } = 25.0;
    public double MaxLight { get; set; } = 60.0;
    public bool IsEnabled { get; set; } = true;
    public int CooldownMinutes { get; set; } = 10;
}
