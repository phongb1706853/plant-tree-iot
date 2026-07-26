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
/// QoS 0, không retained. Ngoài việc lưu telemetry, BE còn TỰ ĐỘNG TƯỚI: xét soil_percent theo
/// ngưỡng (DeviceConfig, fallback mặc định) rồi publish {"pump":..., "mode":"auto"} xuống xmini/control
/// khi cờ mode do server giữ (DeviceModeStore) đang "auto" (logic ở AutoWateringDecider). Kèm mode:"auto"
/// để firmware không rớt khỏi auto khi nhận pump. Điều khiển tay vẫn qua ControlController.
/// </summary>
public class MqttBackgroundService : BackgroundService
{
    public const string TopicSensorData = "xmini/sensor_data";
    public const string TopicConfig = "xmini/config";

    private readonly MongoDbService _mongoDbService;
    private readonly MqttPublisherService _mqttPublisher;
    private readonly DeviceModeStore _modeStore;
    private readonly NotifyClient _notifyClient;
    private readonly ILogger<MqttBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private readonly int _offlineThresholdS;
    private IMqttClient? _mqttClient;

    // Topic dùng chung không mang device_id. Payload xmini/config CŨNG không có device_id
    // (mqtt-api.md mục 3) nên gán cấu hình cho thiết bị telemetry gần nhất vừa thấy.
    private volatile string? _lastSeenDeviceId;

    // Trạng thái auto tưới theo từng thiết bị (đọc/ghi trong _stateLock).
    // _lastPumpOn: trạng thái bơm ở telemetry trước (phát hiện chuyển BẬT->TẮT để mở cooldown).
    // _cooldownUntil: mốc UTC hết cooldown; trước mốc này không auto BẬT lại.
    private readonly object _stateLock = new();
    private readonly Dictionary<string, bool?> _lastPumpOn = new();
    private readonly Dictionary<string, DateTime> _cooldownUntil = new();

    // Trạng thái phát hiện sự kiện Notify (đọc/ghi trong _stateLock).
    // _lastHealth: ảnh chụp cờ sức khỏe telemetry trước (để so edge).
    // _lastSeenUtc: mốc telemetry gần nhất mỗi thiết bị (để quét mất kết nối).
    // _offlineNotified: đã báo device.offline chưa (chặn báo lặp; xoá khi telemetry trở lại).
    private readonly Dictionary<string, DeviceHealthSnapshot> _lastHealth = new();
    private readonly Dictionary<string, DateTime> _lastSeenUtc = new();
    private readonly HashSet<string> _offlineNotified = new();

    public MqttBackgroundService(
        MongoDbService mongoDbService,
        MqttPublisherService mqttPublisher,
        DeviceModeStore modeStore,
        NotifyClient notifyClient,
        ILogger<MqttBackgroundService> logger,
        IConfiguration configuration)
    {
        _mongoDbService = mongoDbService;
        _mqttPublisher = mqttPublisher;
        _modeStore = modeStore;
        _notifyClient = notifyClient;
        _logger = logger;
        _configuration = configuration;

        // Ngưỡng im lặng để coi là mất kết nối (giây). Telemetry ~10s -> mặc định 240s (4 phút).
        _offlineThresholdS = int.TryParse(
            Environment.GetEnvironmentVariable("NOTIFY_OFFLINE_SECONDS") ?? configuration["Notify:OfflineSeconds"],
            out var s) && s > 0 ? s : 240;
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

            // Quét mất kết nối định kỳ. CHỈ khi đang nối broker — nếu server mất broker thì mọi
            // thiết bị sẽ trông như offline (báo giả), nên bỏ qua trong lúc đó.
            if (_mqttClient.IsConnected)
            {
                try { await ScanOfflineAsync(); }
                catch (Exception ex) { _logger.LogError(ex, "Lỗi khi quét thiết bị mất kết nối"); }
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

        await EvaluateNotifyEventsAsync(telemetry, sensorData);
        await EvaluateAutoWateringAsync(telemetry);
    }

    // Phát hiện sự kiện thông báo theo EDGE rồi đẩy sang Notify (best-effort). Cập nhật mốc lastSeen
    // và xoá cờ offline (telemetry về nghĩa là online). Logic thuần ở NotifyEventDetector.
    private async Task EvaluateNotifyEventsAsync(XminiTelemetry telemetry, SensorData sensorData)
    {
        var deviceId = telemetry.DeviceId!;
        var snapshot = new DeviceHealthSnapshot(
            WaterOk: telemetry.WaterOk,
            BattCut: telemetry.BattCut,
            LowBatt: telemetry.LowBatt,
            SoilPercent: telemetry.SoilPercent,
            BatteryPercent: sensorData.BatteryPercent, // đã quy -1 -> null
            BatteryVoltageV: telemetry.BatteryVoltageV);

        IReadOnlyList<NotifyEvent> events;
        bool cameOnline;
        lock (_stateLock)
        {
            DeviceHealthSnapshot? prev = _lastHealth.TryGetValue(deviceId, out var p) ? p : null;
            events = NotifyEventDetector.Detect(prev, snapshot, deviceId);
            _lastHealth[deviceId] = snapshot;

            _lastSeenUtc[deviceId] = DateTime.UtcNow;
            cameOnline = _offlineNotified.Remove(deviceId);
        }

        if (cameOnline)
            _logger.LogInformation("Thiết bị {DeviceId} có telemetry trở lại (online)", deviceId);

        foreach (var evt in events)
            await _notifyClient.SendAsync(evt);
    }

    // Quét các thiết bị đã im lặng quá ngưỡng -> bắn device.offline (1 lần/đợt). Logic thuần ở OfflineMonitor.
    private async Task ScanOfflineAsync()
    {
        var now = DateTime.UtcNow;
        var toNotify = new List<(string DeviceId, int SilenceS)>();

        lock (_stateLock)
        {
            foreach (var (deviceId, lastSeen) in _lastSeenUtc)
            {
                var already = _offlineNotified.Contains(deviceId);
                if (OfflineMonitor.ShouldFireOffline(lastSeen, now, already, _offlineThresholdS))
                {
                    _offlineNotified.Add(deviceId);
                    toNotify.Add((deviceId, (int)(now - lastSeen).TotalSeconds));
                }
            }
        }

        foreach (var (deviceId, silenceS) in toNotify)
        {
            var evt = new NotifyEvent(deviceId, "device.offline", NotifySeverity.Warning,
                new Dictionary<string, object?> { ["lastSeenSecondsAgo"] = silenceS });
            await _notifyClient.SendAsync(evt);
        }
    }

    // Tự động tưới: dựa trên telemetry vừa nhận, quyết định bật/tắt bơm rồi publish xuống xmini/control.
    // Ngưỡng lấy từ DeviceConfig đã lưu (fallback mặc định). Cooldown mở khi bơm chuyển BẬT->TẮT
    // (do server tắt, đủ ẩm, hay firmware tự tắt an toàn — đều thấy qua telemetry pump_on).
    private async Task EvaluateAutoWateringAsync(XminiTelemetry telemetry)
    {
        var deviceId = telemetry.DeviceId!;

        var cfg = await _mongoDbService.GetDeviceConfigAsync(deviceId);
        int onPct     = cfg?.SoilOnPct     ?? AutoWateringDecider.DefaultSoilOnPct;
        int offPct    = cfg?.SoilOffPct    ?? AutoWateringDecider.DefaultSoilOffPct;
        int cooldownS = cfg?.PumpCooldownS ?? AutoWateringDecider.DefaultPumpCooldownS;

        bool cooldownActive;
        lock (_stateLock)
        {
            if (_lastPumpOn.TryGetValue(deviceId, out var prev) && prev == true && telemetry.PumpOn == false)
                _cooldownUntil[deviceId] = DateTime.UtcNow.AddSeconds(cooldownS);
            _lastPumpOn[deviceId] = telemetry.PumpOn;

            cooldownActive = _cooldownUntil.TryGetValue(deviceId, out var until) && DateTime.UtcNow < until;
        }

        // Gate theo mode do SERVER làm chủ (không phải telemetry.Mode — firmware ép manual mỗi khi
        // nhận pump/light nên telemetry không đáng tin làm nguồn sự thật cho auto).
        var serverMode = await _modeStore.GetAsync(deviceId);
        var action = AutoWateringDecider.Decide(
            serverMode, telemetry.SoilPercent, telemetry.PumpOn, onPct, offPct, cooldownActive);
        if (action == PumpAction.None)
            return;

        // GỘP mode:"auto" vào lệnh: firmware xử lý pump trước (ép manual) rồi mode sau (kéo lại auto),
        // nên auto-tưới không còn tự làm thiết bị rớt khỏi auto.
        var pump = action == PumpAction.TurnOn;
        var flat = new Dictionary<string, object?> { ["pump"] = pump, ["mode"] = "auto" };

        if (!await _mqttPublisher.PublishControlAsync(deviceId, flat))
        {
            _logger.LogWarning("Auto-water: publisher chưa kết nối broker, bỏ lệnh pump={Pump} cho {DeviceId}", pump, deviceId);
            return;
        }

        await _mongoDbService.InsertControlCommandAsync(new ControlCommand
        {
            DeviceId = deviceId,
            Payload = flat,
            CreatedAt = DateTime.UtcNow,
        });

        _logger.LogInformation(
            "Auto-water: {DeviceId} soil={Soil}% mode={Mode} -> pump={Pump} (on<{On} off>{Off})",
            deviceId, telemetry.SoilPercent, telemetry.Mode, pump, onPct, offPct);
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
