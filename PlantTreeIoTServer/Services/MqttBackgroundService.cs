using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using PlantTreeIoTServer.Models;
using System.Text;
using System.Text.Json;

namespace PlantTreeIoTServer.Services;

public class MqttBackgroundService : BackgroundService
{
    private readonly MongoDbService _mongoDbService;
    private readonly ILogger<MqttBackgroundService> _logger;
    private readonly IConfiguration _configuration;
    private IMqttClient? _mqttClient;

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

        var factory = new MqttFactory();
        _mqttClient = factory.CreateMqttClient();

        var optionsBuilder = new MqttClientOptionsBuilder()
            .WithClientId($"planttree-server-{Guid.NewGuid()}")
            .WithTcpServer(broker, port)
            .WithCleanSession();

        if (!string.IsNullOrEmpty(username))
            optionsBuilder = optionsBuilder.WithCredentials(username, password);

        if (useTls)
            optionsBuilder = optionsBuilder.WithTlsOptions(o => o.UseTls());

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

                    var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                        .WithTopicFilter("planttree/+/sensors")
                        .WithTopicFilter("xmini/sensor_data")
                        .Build();
                    await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("Subscribed to planttree/+/sensors and xmini/sensor_data");
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

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs e)
    {
        try
        {
            var topic = e.ApplicationMessage.Topic;
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);

            string? deviceId;
            double? soilMoisture;
            double? lightLevel;
            double? temperature;
            double? humidity;
            string commandTopic;

            if (topic == "xmini/sensor_data")
            {
                // Parse xmini board payload
                var xmini = JsonSerializer.Deserialize<XminiSensorPayload>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (xmini?.DeviceId == null) return;

                deviceId    = xmini.DeviceId;
                lightLevel  = xmini.LightLux;
                soilMoisture = null;
                temperature = xmini.TemperatureC;
                humidity    = xmini.HumidityPercent;
                commandTopic = "xmini/control";
            }
            else
            {
                // Parse planttree/{deviceId}/sensors payload
                var parts = topic.Split('/');
                if (parts.Length < 3) return;
                deviceId = parts[1];

                var data = JsonSerializer.Deserialize<MqttSensorPayload>(payload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (data == null) return;

                soilMoisture = data.SoilMoisture;
                lightLevel   = data.LightLevel;
                temperature  = data.Temperature;
                humidity     = data.Humidity;
                commandTopic = $"planttree/{deviceId}/commands";
            }

            _logger.LogInformation("MQTT received from {DeviceId} (topic={Topic}): light={Light}, moisture={Moisture}",
                deviceId, topic, lightLevel, soilMoisture);

            // Save to MongoDB
            await _mongoDbService.InsertSensorDataAsync(new SensorData
            {
                DeviceId     = deviceId,
                Timestamp    = DateTime.UtcNow,
                Temperature  = temperature,
                Humidity     = humidity,
                SoilMoisture = soilMoisture,
                LightLevel   = lightLevel
            });
            await _mongoDbService.UpdateDeviceLastSeenAsync(deviceId);

            // Evaluate rules and publish commands
            await EvaluateAndPublishCommandsAsync(deviceId, soilMoisture, lightLevel, commandTopic);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling MQTT message on topic {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private async Task EvaluateAndPublishCommandsAsync(string deviceId, double? soilMoisture, double? lightLevel, string commandTopic = "")
    {
        var now = DateTime.UtcNow;
        var commands = new List<ControlCommand>();

        // Moisture rules
        if (soilMoisture != null)
        {
            var moistureRules = await _mongoDbService.GetMoistureRulesAsync(deviceId);
            foreach (var rule in moistureRules)
            {
                if (!rule.IsEnabled) continue;
                if (rule.LastTriggeredAt.HasValue &&
                    (now - rule.LastTriggeredAt.Value).TotalMinutes < rule.CooldownMinutes) continue;

                ControlCommand? cmd = null;
                if (soilMoisture < rule.MinMoisture)
                    cmd = new ControlCommand { DeviceId = deviceId, Command = "WATER_ON",
                        Parameters = new Dictionary<string, object> { { "duration", rule.WaterDurationMs }, { "reason", "moisture_rule" }, { "ruleId", rule.Id! }, { "currentMoisture", soilMoisture } },
                        Executed = false, CreatedAt = now };
                else if (soilMoisture >= rule.MaxMoisture)
                    cmd = new ControlCommand { DeviceId = deviceId, Command = "WATER_OFF",
                        Parameters = new Dictionary<string, object> { { "reason", "moisture_rule" }, { "ruleId", rule.Id! }, { "currentMoisture", soilMoisture } },
                        Executed = false, CreatedAt = now };

                if (cmd != null)
                {
                    await _mongoDbService.InsertControlCommandAsync(cmd);
                    await _mongoDbService.UpdateRuleLastTriggeredAsync(rule.Id!);
                    commands.Add(cmd);
                    _logger.LogInformation("Moisture rule triggered {Command} for {DeviceId}", cmd.Command, deviceId);
                }
            }
        }

        // Light rules
        if (lightLevel != null)
        {
            var lightRules = await _mongoDbService.GetLightRulesAsync(deviceId);
            foreach (var rule in lightRules)
            {
                if (!rule.IsEnabled) continue;
                if (rule.LastTriggeredAt.HasValue &&
                    (now - rule.LastTriggeredAt.Value).TotalMinutes < rule.CooldownMinutes) continue;

                ControlCommand? cmd = null;
                if (lightLevel < rule.MinLight)
                    cmd = new ControlCommand { DeviceId = deviceId, Command = "LIGHT_ON",
                        Parameters = new Dictionary<string, object> { { "reason", "light_rule" }, { "ruleId", rule.Id! }, { "currentLight", lightLevel } },
                        Executed = false, CreatedAt = now };
                else if (lightLevel >= rule.MaxLight)
                    cmd = new ControlCommand { DeviceId = deviceId, Command = "LIGHT_OFF",
                        Parameters = new Dictionary<string, object> { { "reason", "light_rule" }, { "ruleId", rule.Id! }, { "currentLight", lightLevel } },
                        Executed = false, CreatedAt = now };

                if (cmd != null)
                {
                    await _mongoDbService.InsertControlCommandAsync(cmd);
                    await _mongoDbService.UpdateLightRuleLastTriggeredAsync(rule.Id!);
                    commands.Add(cmd);
                    _logger.LogInformation("Light rule triggered {Command} for {DeviceId} (light={L})", cmd.Command, deviceId, lightLevel);
                }
            }
        }

        // Publish all commands via MQTT
        var topic = string.IsNullOrEmpty(commandTopic) ? $"planttree/{deviceId}/commands" : commandTopic;
        foreach (var command in commands)
        {
            var payload = JsonSerializer.Serialize(new
            {
                command = command.Command,
                commandId = command.Id,
                parameters = command.Parameters
            });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient!.PublishAsync(message);
            _logger.LogInformation("Published {Command} to topic {Topic}", command.Command, topic);
        }
    }
}

public class MqttSensorPayload
{
    public double? Temperature { get; set; }
    public double? Humidity { get; set; }
    public double? SoilMoisture { get; set; }
    public double? LightLevel { get; set; }
    public double? WaterLevel { get; set; }
    public double? PhLevel { get; set; }
}

public class XminiSensorPayload
{
    [System.Text.Json.Serialization.JsonPropertyName("device_id")]
    public string? DeviceId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("temperature_c")]
    public double? TemperatureC { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("humidity_percent")]
    public double? HumidityPercent { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("light_lux")]
    public double? LightLux { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("pressure_hpa")]
    public double? PressureHpa { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("altitude_m")]
    public double? AltitudeM { get; set; }
}
