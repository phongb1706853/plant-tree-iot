using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Text.Json;

namespace PlantTreeIoTServer.Services;

public class MqttPublisherService
{
    private IMqttClient? _mqttClient;
    private readonly ILogger<MqttPublisherService> _logger;
    private bool _isConnected;

    public MqttPublisherService(ILogger<MqttPublisherService> logger)
    {
        _logger = logger;
        _isConnected = false;
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

            var factory = new MqttFactory();
            _mqttClient = factory.CreateMqttClient();

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithClientId($"planttree-publisher-{Guid.NewGuid()}")
                .WithTcpServer(broker, port)
                .WithCleanSession();

            if (!string.IsNullOrEmpty(username))
                optionsBuilder = optionsBuilder.WithCredentials(username, password);

            if (useTls)
                optionsBuilder = optionsBuilder.WithTlsOptions(o => o.UseTls());

            var options = optionsBuilder.Build();
            await _mqttClient.ConnectAsync(options);
            _isConnected = true;

            _logger.LogInformation("MQTT Publisher connected to {Broker}:{Port}", broker, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MQTT Publisher");
            _isConnected = false;
        }
    }

    public async Task PublishCommandAsync(string deviceId, string command, Dictionary<string, object>? parameters = null)
    {
        if (!_isConnected || _mqttClient == null)
        {
            _logger.LogWarning("MQTT Publisher not connected, cannot publish command");
            return;
        }

        try
        {
            // Determine topic based on device ID
            string topic = deviceId.Contains("xmini") ? "xmini/control" : $"planttree/{deviceId}/commands";

            var payload = JsonSerializer.Serialize(new
            {
                command = command,
                parameters = parameters ?? new Dictionary<string, object>()
            });

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithRetainFlag(false)
                .Build();

            await _mqttClient.PublishAsync(message);
            _logger.LogInformation("Published {Command} to {Topic} for device {DeviceId}", command, topic, deviceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing MQTT command for device {DeviceId}", deviceId);
        }
    }

    public bool IsConnected => _isConnected;
}
