using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Net.Security;
using System.Security.Authentication;
using System.Text.Json;

namespace PlantTreeIoTServer.Services;

public class MqttPublisherService
{
    private IMqttClient? _mqttClient;
    private MqttClientOptions? _options; // giữ lại để tự nối lại khi broker ngắt (mạng rớt / HiveMQ đá vì thiếu quyền topic)
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private readonly ILogger<MqttPublisherService> _logger;

    public MqttPublisherService(ILogger<MqttPublisherService> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync(IConfiguration configuration)
    {
        var broker = Environment.GetEnvironmentVariable("MQTT_BROKER")
            ?? configuration["Mqtt:Broker"];

        if (string.IsNullOrEmpty(broker))
        {
            _logger.LogWarning("MQTT_BROKER not configured, MQTT publishing disabled");
            return;
        }

        try
        {
            var port = int.Parse(Environment.GetEnvironmentVariable("MQTT_PORT")
                ?? configuration["Mqtt:Port"] ?? "8883");
            var username = Environment.GetEnvironmentVariable("MQTT_USERNAME")
                ?? configuration["Mqtt:Username"] ?? "";
            var password = Environment.GetEnvironmentVariable("MQTT_PASSWORD")
                ?? configuration["Mqtt:Password"] ?? "";
            var useTls = bool.Parse(Environment.GetEnvironmentVariable("MQTT_USE_TLS")
                ?? configuration["Mqtt:UseTls"] ?? "true");
            var allowInvalidCert = bool.Parse(Environment.GetEnvironmentVariable("MQTT_ALLOW_INVALID_CERT")
                ?? configuration["Mqtt:AllowInvalidCert"] ?? "false");

            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithClientId($"planttree-publisher-{Guid.NewGuid()}")
                .WithTcpServer(broker, port)
                .WithCleanSession();

            if (!string.IsNullOrEmpty(username))
                optionsBuilder = optionsBuilder.WithCredentials(username, password);

            if (useTls)
                optionsBuilder = optionsBuilder.WithTlsOptions(o =>
                {
                    o.UseTls(true);
                    o.WithTargetHost(broker); // ensure SNI matches the broker's certificate
                    o.WithSslProtocols(SslProtocols.Tls12 | SslProtocols.Tls13);
                    o.WithCertificateValidationHandler(ctx =>
                    {
                        if (ctx.SslPolicyErrors == SslPolicyErrors.None)
                            return true;

                        _logger.LogWarning("MQTT TLS certificate validation failed: {Errors} (subject={Subject})",
                            ctx.SslPolicyErrors, ctx.Certificate?.Subject);

                        return allowInvalidCert;
                    });
                });

            _options = optionsBuilder.Build();
            await _mqttClient.ConnectAsync(_options);

            _logger.LogInformation("MQTT Publisher connected to {Broker}:{Port}", broker, port);
        }
        catch (Exception ex)
        {
            // Giữ client + options: lần publish sau sẽ thử nối lại (TryReconnectAsync) thay vì tắt hẳn.
            _logger.LogError(ex, "Failed to initialize MQTT Publisher");
        }
    }

    /// <summary>Nối lại broker nếu đang rớt (một luồng nối tại một thời điểm). true nếu đã/đang kết nối.</summary>
    private async Task<bool> TryReconnectAsync()
    {
        await _connectLock.WaitAsync();
        try
        {
            if (_mqttClient!.IsConnected) return true;
            await _mqttClient.ConnectAsync(_options!);
            _logger.LogInformation("MQTT Publisher reconnected");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT Publisher reconnect failed");
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    /// <summary>
    /// Publish một object JSON PHẲNG xuống topic điều khiển (QoS 0, không retained) — đúng hợp đồng
    /// firmware. Thiết bị thật: xmini/control (dùng chung). Thiết bị ảo SIM_*: xmini-sim/{id}/control
    /// (xem SimTopics) — nên lệnh cho thiết bị ảo KHÔNG BAO GIỜ tới ESP32 thật. Payload y hệt nhau.
    /// Theo hợp đồng
    /// firmware. Thiết bị chỉ hiểu các khoá phẳng: pump / light / light_pwm / mode / auto / config /
    /// message / message_secs. Khoá lạ bị bỏ qua. KHÔNG bọc trong {"command":...,"parameters":...}.
    /// </summary>
    /// <returns>true nếu đã gửi được lên broker.</returns>
    public async Task<bool> PublishControlAsync(string deviceId, IDictionary<string, object?> flatKeys)
    {
        if (_mqttClient == null || _options == null)
        {
            _logger.LogWarning("MQTT Publisher not configured, cannot publish control for {DeviceId}", deviceId);
            return false;
        }

        // Broker có thể đã ngắt (mạng, hoặc HiveMQ đá client publish lên topic không được phép) -> nối lại trước.
        if (!_mqttClient.IsConnected && !await TryReconnectAsync())
        {
            _logger.LogWarning("MQTT Publisher not connected, cannot publish control for {DeviceId}", deviceId);
            return false;
        }

        try
        {
            var payload = JsonSerializer.Serialize(flatKeys);

            var topic = SimTopics.ControlTopicFor(deviceId);
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce) // QoS 0 theo hợp đồng
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.PublishAsync(message);
            _logger.LogInformation("Published control to {Topic} for {DeviceId}: {Payload}", topic, deviceId, payload);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing MQTT control for device {DeviceId}", deviceId);
            return false;
        }
    }

    public bool IsConnected => _mqttClient?.IsConnected == true;
}
