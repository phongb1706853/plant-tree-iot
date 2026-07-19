using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using PlantTreeIoTServer.Models;
using System.Net.Security;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;

namespace PlantTreeIoTServer.Services;

/// <summary>
/// Nghe MQTT theo hợp đồng firmware Xmini (mqtt-api.md):
///   - xmini/sensor_data : telemetry ~10s (21 trường phẳng)  -> lưu SensorData
///   - xmini/config      : ngưỡng auto thiết bị đang dùng      -> upsert DeviceConfig
/// QoS 0, không retained. Thiết bị TỰ chạy auto; BE không sinh lệnh tưới/đèn ở đây nữa
/// (điều khiển đi qua ControlController -> xmini/control dưới dạng khoá phẳng).
/// </summary>
public class MqttBackgroundService : BackgroundService
{
    public const string TopicSensorData = "xmini/sensor_data";
    public const string TopicConfig = "xmini/config";

    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<MqttBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private IMqttClient? _mqttClient;

    // Topic dùng chung không mang device_id. Payload xmini/config CŨNG không có device_id
    // (mqtt-api.md mục 3) nên gán cấu hình cho thiết bị telemetry gần nhất vừa thấy.
    private volatile string? _lastSeenDeviceId;

    public MqttBackgroundService(
        MongoDbService mongoDbService,
        ILogger<MqttBackgroundService> logger,
        IConfiguration configuration)
    {
        _mongoDbService = mongoDbService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var broker = Environment.GetEnvironmentVariable("MQTT_BROKER")
            ?? _configuration["Mqtt:Broker"];

        if (string.IsNullOrEmpty(broker))
        {
            _logger.LogWarning("MQTT_BROKER not configured, MQTT service disabled");
            return;
        }

        var port = int.Parse(Environment.GetEnvironmentVariable("MQTT_PORT")
            ?? _configuration["Mqtt:Port"] ?? "8883");
        var username = Environment.GetEnvironmentVariable("MQTT_USERNAME")
            ?? _configuration["Mqtt:Username"] ?? "";
        var password = Environment.GetEnvironmentVariable("MQTT_PASSWORD")
            ?? _configuration["Mqtt:Password"] ?? "";
        var useTls = bool.Parse(Environment.GetEnvironmentVariable("MQTT_USE_TLS")
            ?? _configuration["Mqtt:UseTls"] ?? "true");
        var allowInvalidCert = bool.Parse(Environment.GetEnvironmentVariable("MQTT_ALLOW_INVALID_CERT")
            ?? _configuration["Mqtt:AllowInvalidCert"] ?? "false");

        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"planttree-server-{Guid.NewGuid()}")
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

        var options = optionsBuilder.Build();
        _mqttClient.ApplicationMessageReceivedAsync += HandleMessageAsync;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_mqttClient.IsConnected)
                {
                    await _mqttClient.ConnectAsync(options, stoppingToken);
                    _logger.LogInformation("Connected to MQTT broker: {Broker}:{Port}", broker, port);

                    // QoS 0 cho subscribe (hợp đồng). BE nên subscribe TRƯỚC khi thiết bị connect
                    // vì broker không giữ giá trị cuối (không retained).
                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter(TopicSensorData, MqttQualityOfServiceLevel.AtMostOnce)
                        .WithTopicFilter(TopicConfig, MqttQualityOfServiceLevel.AtMostOnce)
                        .Build();
                    await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("Subscribed to {S} and {C} (QoS 0)", TopicSensorData, TopicConfig);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT connection failed, retrying in 5s");
            }

            await Task.Delay(5000, stoppingToken);
        }

        if (_mqttClient.IsConnected)
            await _mqttClient.DisconnectAsync();
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        try
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            if (topic == TopicSensorData)
                await HandleSensorDataAsync(payload);
            else if (topic == TopicConfig)
                await HandleConfigAsync(payload);
            else
                _logger.LogDebug("Ignoring message on unexpected topic {Topic}", topic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling MQTT message on topic {Topic}", topic);
        }
    }

    private async Task HandleSensorDataAsync(string payload)
    {
        var telemetry = JsonSerializer.Deserialize<XminiTelemetry>(payload, JsonOpts);
        if (telemetry?.DeviceId == null)
        {
            _logger.LogWarning("xmini/sensor_data without device_id, skipping");
            return;
        }

        _lastSeenDeviceId = telemetry.DeviceId;

        var sensorData = telemetry.ToSensorData();
        await _mongoDbService.InsertSensorDataAsync(sensorData);
        await _mongoDbService.UpdateDeviceLastSeenAsync(telemetry.DeviceId);

        _logger.LogInformation(
            "Telemetry from {DeviceId}: mode={Mode} soil={Soil}% light={Lux}lux batt={Batt}% pump={Pump} lightOn={Light}",
            telemetry.DeviceId, telemetry.Mode, telemetry.SoilPercent, telemetry.LightLux,
            sensorData.BatteryPercent, telemetry.PumpOn, telemetry.LightOn);
    }

    private async Task HandleConfigAsync(string payload)
    {
        var envelope = JsonSerializer.Deserialize<XminiConfigEnvelope>(payload, JsonOpts);
        if (envelope?.Config == null)
        {
            _logger.LogWarning("xmini/config without 'config' object, skipping");
            return;
        }

        // Payload config không mang device_id -> gán cho thiết bị telemetry gần nhất.
        var deviceId = _lastSeenDeviceId;
        if (string.IsNullOrEmpty(deviceId))
        {
            _logger.LogWarning("Received xmini/config but no device seen yet; cannot attribute config, skipping");
            return;
        }

        await _mongoDbService.UpsertDeviceConfigAsync(envelope.Config.ToDeviceConfig(deviceId));
        _logger.LogInformation("Stored device config for {DeviceId} from xmini/config", deviceId);
    }
}
