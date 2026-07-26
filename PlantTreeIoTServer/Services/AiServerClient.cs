using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PlantTreeIoTServer.Services;

/// <summary>
/// Client gọi AI server (tree-grow-helper) qua endpoint OpenAI-compatible:
///   POST /v1/chat/completions  { model, messages[] , ... }  -> { choices[], usage, ... }
///
/// Endpoint STATELESS: caller gửi lại TOÀN BỘ messages[] mỗi lượt. Xác nhận điều khiển làm
/// bằng cách giữ lại assistant message (kèm tool_calls) trong messages[] rồi thêm câu trả lời
/// "có"/"không" của user — AI server sẽ thực thi hoặc huỷ. Vì vậy .NET chỉ proxy MỎNG, không lưu state.
///
/// .NET server-side ép 3 trường trước khi forward:
///   - user   = userId (lấy từ JWT; KHÔNG tin giá trị App gửi) — để AI scope "thiết bị của bạn".
///   - stream = false (v1 chưa relay SSE).
///   - model  = "plant-assistant" nếu App không gửi (passthrough nếu có).
///
/// Lời gọi này là NỘI BỘ (.NET -> AI) nên KHÔNG gắn JWT. JWT chỉ áp cho lời gọi VÀO .NET API.
/// </summary>
public class AiServerClient
{
    private const string DefaultModel = "plant-assistant";
    private const string CompletionsPath = "/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly ILogger<AiServerClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AiServerClient(HttpClient http, ILogger<AiServerClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// Forward một request OpenAI-compatible tới AI server. <paramref name="body"/> là request thô
    /// App gửi (đã validate có messages[]); phương thức này ép user/stream/model rồi trả nguyên
    /// response JSON để relay về App.
    /// </summary>
    public async Task<JsonNode> ChatCompletionsAsync(string userId, JsonObject body, CancellationToken ct = default)
    {
        // Ép các trường server-side (ghi đè mọi giá trị App gửi).
        body["user"] = userId;
        body["stream"] = false;
        if (body["model"] is null || body["model"] is JsonValue mv && string.IsNullOrWhiteSpace(mv.ToString()))
            body["model"] = DefaultModel;

        var content = new StringContent(body.ToJsonString(JsonOpts), Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsync(CompletionsPath, content, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Không kết nối được / timeout -> coi như AI server không sẵn sàng.
            _logger.LogWarning(ex, "AI server không phản hồi khi gọi {Path}", CompletionsPath);
            throw new AiServerUnavailableException(
                "Không kết nối được AI server. Kiểm tra AI_SERVER_URL / AiServer:BaseUrl và AI server đã chạy chưa.", ex);
        }

        var json = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // AI server chưa cấu hình LLM (/setup) -> relay 503 với message rõ ràng.
            _logger.LogWarning("AI server trả 503 (chưa cấu hình?) khi gọi {Path}: {Body}", CompletionsPath, json);
            throw new AiServerUnavailableException(
                "AI server chưa cấu hình (LLM). Vào /setup của AI server để cấu hình.", null);
        }

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("AI server lỗi {Status} khi gọi {Path}: {Body}", (int)resp.StatusCode, CompletionsPath, json);
            throw new AiServerException($"AI server trả {(int)resp.StatusCode}.", (int)resp.StatusCode);
        }

        try
        {
            return JsonNode.Parse(json) ?? throw new JsonException("response rỗng");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Không parse được phản hồi AI server khi gọi {Path}: {Body}", CompletionsPath, json);
            throw new AiServerException("Phản hồi AI server không hợp lệ.", (int)HttpStatusCode.BadGateway);
        }
    }
}

/// <summary>AI server không kết nối được hoặc chưa cấu hình -> map thành 503.</summary>
public class AiServerUnavailableException : Exception
{
    public AiServerUnavailableException(string message, Exception? inner) : base(message, inner) { }
}

/// <summary>AI server trả lỗi khác (map thành 502/tương ứng).</summary>
public class AiServerException : Exception
{
    public int StatusCode { get; }
    public AiServerException(string message, int statusCode) : base(message) => StatusCode = statusCode;
}
