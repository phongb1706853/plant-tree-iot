using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

/// <summary>
/// Proxy trợ lý AI (OpenAI-compatible): App -> .NET (JWT) -> AI server (tree-grow-helper) -> MCP -> gọi ngược .NET.
///
/// Endpoint AI server là STATELESS: App giữ toàn bộ messages[] và gửi lại mỗi lượt. .NET chỉ proxy
/// MỎNG — không lưu hội thoại, không còn bước /confirm riêng. Xác nhận điều khiển: App giữ lại
/// assistant message (kèm tool_calls) trong messages[] rồi thêm câu trả lời "có"/"không".
///
/// userId LUÔN lấy từ JWT (App không tự khai) và được .NET ép vào trường 'user' của request.
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
    /// Chat completions tương thích OpenAI (chạy toàn bộ agent: RAG + điều khiển IoT + xác nhận).
    /// Body: request OpenAI thô { model?, messages[], ... }. Response: giữ nguyên từ AI server
    /// ({ choices[], usage, ... }); khi cần xác nhận, choices[].message có tool_calls và content là câu hỏi Có/Không.
    /// .NET ép user = userId (JWT), stream = false; model mặc định "plant-assistant".
    /// </summary>
    [HttpPost("v1/chat/completions")]
    public async Task<IActionResult> ChatCompletions([FromBody] JsonNode? body)
    {
        if (body is not JsonObject obj)
            return BadRequest("Body phải là JSON object OpenAI-compatible ({ messages: [...] }).");

        if (obj["messages"] is not JsonArray messages || messages.Count == 0)
            return BadRequest("'messages' là bắt buộc và không được rỗng.");

        try
        {
            var result = await _ai.ChatCompletionsAsync(UserId, obj, HttpContext.RequestAborted);
            return Ok(result);
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
