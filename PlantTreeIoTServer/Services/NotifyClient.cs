using System.Net.Http.Json;

namespace PlantTreeIoTServer.Services;

/// <summary>
/// Client đẩy sự kiện sang Notify service: POST {NOTIFY_URL}/internal/notify (header x-api-key).
/// BEST-EFFORT — lỗi mạng / lỗi HTTP KHÔNG ném ra (không được chặn pipeline telemetry / auto-tưới).
/// Chưa cấu hình (thiếu NOTIFY_URL hoặc NOTIFY_API_KEY) -> no-op, chỉ log 1 lần lúc khởi tạo.
///
/// Singleton: dùng IHttpClientFactory (named client "notify") để tránh captive-dependency khi được
/// inject vào MqttBackgroundService (cũng singleton). Xem NOTIFY-INTEGRATION-GUIDE.md.
/// </summary>
public class NotifyClient
{
    public const string HttpClientName = "notify";
    private const string NotifyPath = "/internal/notify";

    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<NotifyClient> _logger;
    private readonly string? _apiKey;

    /// <summary>True khi có đủ NOTIFY_URL + NOTIFY_API_KEY để gửi thật.</summary>
    public bool Enabled { get; }

    public NotifyClient(IHttpClientFactory httpFactory, ILogger<NotifyClient> logger, IConfiguration configuration)
    {
        _httpFactory = httpFactory;
        _logger = logger;

        var url = Environment.GetEnvironmentVariable("NOTIFY_URL")
            ?? configuration["Notify:BaseUrl"];
        _apiKey = Environment.GetEnvironmentVariable("NOTIFY_API_KEY")
            ?? configuration["Notify:ApiKey"];

        Enabled = !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(_apiKey);

        if (!Enabled)
            _logger.LogInformation(
                "NotifyClient chưa cấu hình (NOTIFY_URL/NOTIFY_API_KEY) -> BỎ QUA gửi thông báo cho team Notify.");
    }

    /// <summary>Gửi 1 sự kiện. Trả true nếu Notify nhận (2xx). Không ném lỗi — best-effort.</summary>
    public async Task<bool> SendAsync(NotifyEvent evt, CancellationToken ct = default)
    {
        if (!Enabled) return false;

        var payload = NotifyPayload.Build(evt, DateTime.UtcNow);

        try
        {
            var http = _httpFactory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Post, NotifyPath)
            {
                Content = JsonContent.Create(payload),
            };
            req.Headers.Add("x-api-key", _apiKey);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Notify trả {Status} cho event {Event} ({DeviceId}): {Body}",
                    (int)resp.StatusCode, evt.EventCode, evt.DeviceId, body);
                return false;
            }

            _logger.LogInformation("Đã gửi Notify event {Event} cho {DeviceId}", evt.EventCode, evt.DeviceId);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Không gửi được Notify event {Event} cho {DeviceId} (mạng/timeout)",
                evt.EventCode, evt.DeviceId);
            return false;
        }
    }
}
