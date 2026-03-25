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
                        .Build();
                    await _mqttClient.SubscribeAsync(subscribeOptions, stoppingToken);
                    _logger.LogInformation("Subscribed to planttree/+/sensors");
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

            // Extract deviceId from: planttree/{deviceId}/sensors
            var parts = topic.Split('/');
            if (parts.Length < 3) return;
            var deviceId = parts[1];

            _logger.LogInformation("MQTT received from {DeviceId}: {Payload}", deviceId, payload);

            var data = JsonSerializer.Deserialize<MqttSensorPayload>(payload,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data == null) return;

            // Save sensor data to MongoDB
            var sensorData = new SensorData
            {
                DeviceId = deviceId,
                Timestamp = DateTime.UtcNow,
                Temperature = data.Temperature,
                Humidity = data.Humidity,
                SoilMoisture = data.SoilMoisture,
                LightLevel = data.LightLevel,
                WaterLevel = data.WaterLevel,
                PhLevel = data.PhLevel
            };

            await _mongoDbService.InsertSensorDataAsync(sensorData);
            await _mongoDbService.UpdateDeviceLastSeenAsync(deviceId);

            // Evaluate moisture rules and publish commands
            await EvaluateAndPublishCommandsAsync(deviceId, data.SoilMoisture);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling MQTT message on topic {Topic}", e.ApplicationMessage.Topic);
        }
    }

    private async Task EvaluateAndPublishCommandsAsync(string deviceId, double? soilMoisture)
    {
        if (soilMoisture == null) return;

        var rules = await _mongoDbService.GetMoistureRulesAsync(deviceId);
        var now = DateTime.UtcNow;

        foreach (var rule in rules)
        {
            if (!rule.IsEnabled) continue;

            if (rule.LastTriggeredAt.HasValue &&
                (now - rule.LastTriggeredAt.Value).TotalMinutes < rule.CooldownMinutes)
                continue;

            ControlCommand? command = null;

            if (soilMoisture < rule.MinMoisture)
            {
                command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "WATER_ON",
                    Parameters = new Dictionary<string, object>
                    {
                        { "duration", rule.WaterDurationMs },
                        { "reason", "moisture_rule" },
                        { "ruleId", rule.Id! },
                        { "currentMoisture", soilMoisture }
                    },
                    Executed = false,
                    CreatedAt = now
                };

                _logger.LogInformation("Rule '{Name}' triggered WATER_ON for {DeviceId} (moisture={M}%)",
                    rule.Name, deviceId, soilMoisture);
            }
            else if (soilMoisture >= rule.MaxMoisture)
            {
                command = new ControlCommand
                {
                    DeviceId = deviceId,
                    Command = "WATER_OFF",
                    Parameters = new Dictionary<string, object>
                    {
                        { "reason", "moisture_rule" },
                        { "ruleId", rule.Id! },
                        { "currentMoisture", soilMoisture }
                    },
                    Executed = false,
                    CreatedAt = now
                };

                _logger.LogInformation("Rule '{Name}' triggered WATER_OFF for {DeviceId} (moisture={M}%)",
                    rule.Name, deviceId, soilMoisture);
            }

            if (command != null)
            {
                await _mongoDbService.InsertControlCommandAsync(command);
                await _mongoDbService.UpdateRuleLastTriggeredAsync(rule.Id!);

                // Publish command to ESP32 via MQTT
                var commandPayload = JsonSerializer.Serialize(new
                {
                    command = command.Command,
                    commandId = command.Id,
                    parameters = command.Parameters
                });

                var message = new MqttApplicationMessageBuilder()
                    .WithTopic($"planttree/{deviceId}/commands")
                    .WithPayload(commandPayload)
                    .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                    .WithRetainFlag(false)
                    .Build();

                await _mqttClient!.PublishAsync(message);
                _logger.LogInformation("Published {Command} to {DeviceId} via MQTT", command.Command, deviceId);
            }
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
