using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlantTreeIoTServer.Services;

/// <summary>
/// Client gọi AI server (tree-grow-helper) native endpoints:
///   POST /chat          {userId, sessionId, message}          -> {reply, pendingAction|null}
///   POST /chat/confirm  {userId, sessionId, actionId, approved} -> {reply, pendingAction|null}
/// AI server tự nhớ hội thoại + hành động chờ xác nhận theo sessionId (stateful),
/// nên .NET chỉ chuyển tiếp, không lưu state.
///
/// Lời gọi này là NỘI BỘ (.NET -> AI) nên KHÔNG gắn JWT. JWT chỉ áp cho lời gọi VÀO .NET API.
/// </summary>
public class AiServerClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AiServerClient> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public AiServerClient(HttpClient http, ILogger<AiServerClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public Task<AiChatResult> ChatAsync(string userId, string sessionId, string message, CancellationToken ct = default)
        => PostAsync("/chat", new { userId, sessionId, message }, ct);

    public Task<AiChatResult> ConfirmAsync(string userId, string sessionId, string actionId, bool approved, CancellationToken ct = default)
        => PostAsync("/chat/confirm", new { userId, sessionId, actionId, approved }, ct);

    private async Task<AiChatResult> PostAsync(string path, object body, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsJsonAsync(path, body, JsonOpts, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Không kết nối được / timeout -> coi như AI server không sẵn sàng.
            _logger.LogWarning(ex, "AI server không phản hồi khi gọi {Path}", path);
            throw new AiServerUnavailableException(
                "Không kết nối được AI server. Kiểm tra AI_SERVER_URL / AiServer:BaseUrl và AI server đã chạy chưa.", ex);
        }

        var json = await resp.Content.ReadAsStringAsync(ct);

        if (resp.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            // AI server chưa cấu hình LLM (/setup) -> relay 503 với message gốc.
            _logger.LogWarning("AI server trả 503 (chưa cấu hình?) khi gọi {Path}: {Body}", path, json);
            throw new AiServerUnavailableException(
                "AI server chưa cấu hình (LLM). Vào /setup của AI server để cấu hình.", null);
        }

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogError("AI server lỗi {Status} khi gọi {Path}: {Body}", (int)resp.StatusCode, path, json);
            throw new AiServerException($"AI server trả {(int)resp.StatusCode}.", (int)resp.StatusCode);
        }

        try
        {
            return JsonSerializer.Deserialize<AiChatResult>(json, JsonOpts) ?? new AiChatResult();
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Không parse được phản hồi AI server khi gọi {Path}: {Body}", path, json);
            throw new AiServerException("Phản hồi AI server không hợp lệ.", (int)HttpStatusCode.BadGateway);
        }
    }
}

/// <summary>Phản hồi từ AI server /chat và /chat/confirm. pendingAction giữ nguyên dạng thô để relay.</summary>
public class AiChatResult
{
    [JsonPropertyName("reply")]
    public string Reply { get; set; } = string.Empty;

    [JsonPropertyName("pendingAction")]
    public JsonElement? PendingAction { get; set; }
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
