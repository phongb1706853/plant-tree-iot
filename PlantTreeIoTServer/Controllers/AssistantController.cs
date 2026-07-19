using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

/// <summary>
/// Proxy trợ lý AI: App -> .NET (JWT) -> AI server (tree-grow-helper) -> MCP -> gọi ngược .NET.
/// userId LUÔN lấy từ JWT (App không tự khai). sessionId do App giữ để hỗ trợ multi-turn;
/// thiếu thì mặc định = userId. Response luôn kèm sessionId để App dùng lại.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AssistantController : ControllerBase
{
    private readonly AiServerClient _ai;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(AiServerClient ai, ILogger<AssistantController> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    /// <summary>
    /// Gửi một câu chat cho trợ lý. Nếu là lệnh ĐIỀU KHIỂN, response trả 'pendingAction' (CHƯA thực thi)
    /// — App hiện Có/Không rồi gọi /confirm với pendingAction.id. Câu hỏi/đọc dữ liệu trả lời trực tiếp.
    /// </summary>
    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] AssistantChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return BadRequest("'message' là bắt buộc.");

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? UserId : request.SessionId!;
        try
        {
            var result = await _ai.ChatAsync(UserId, sessionId, request.Message, HttpContext.RequestAborted);
            return Ok(new { reply = result.Reply, pendingAction = result.PendingAction, sessionId });
        }
        catch (Exception ex)
        {
            return HandleAiError(ex);
        }
    }

    /// <summary>Xác nhận (approved=true) hoặc huỷ (false) một hành động điều khiển đang chờ.</summary>
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] AssistantConfirmRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.ActionId))
            return BadRequest("'sessionId' và 'actionId' là bắt buộc.");

        try
        {
            var result = await _ai.ConfirmAsync(UserId, request.SessionId!, request.ActionId!, request.Approved, HttpContext.RequestAborted);
            return Ok(new { reply = result.Reply, pendingAction = result.PendingAction, sessionId = request.SessionId });
        }
        catch (Exception ex)
        {
            return HandleAiError(ex);
        }
    }

    private IActionResult HandleAiError(Exception ex)
    {
        switch (ex)
        {
            case AiServerUnavailableException:
                return StatusCode(503, ex.Message);
            case AiServerException aiEx:
                return StatusCode(502, aiEx.Message);
            default:
                _logger.LogError(ex, "Lỗi không xác định khi gọi AI server");
                return StatusCode(500, "Internal server error");
        }
    }
}

public class AssistantChatRequest
{
    public string Message { get; set; } = string.Empty;
    public string? SessionId { get; set; }
}

public class AssistantConfirmRequest
{
    public string? SessionId { get; set; }
    public string? ActionId { get; set; }
    public bool Approved { get; set; }
}
