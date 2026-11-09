using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Server;
using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm thử định tuyến thông báo với broker MQTTnet TRONG tiến trình + webhook Notify giả (không ra mạng).
//
// Quy tắc: thiết bị ảo SIM_* -> publish đúng payload Notify lên xmini-sim/{id}/notify (khung "🔔 Thông báo"
// trên dashboard); chỉ gửi webhook Notify thật khi người dùng bật "gửi cả sang Notify thật".
// Thiết bị THẬT -> webhook như cũ và KHÔNG BAO GIỜ lên MQTT.
[Collection("MqttEnv")]
public class NotifyDispatcherTests : IAsyncLifetime
{
    private static readonly string[] EnvKeys =
    {
        "MQTT_BROKER", "MQTT_PORT", "MQTT_USERNAME", "MQTT_PASSWORD", "MQTT_USE_TLS", "MQTT_ALLOW_INVALID_CERT",
        "NOTIFY_URL", "NOTIFY_API_KEY",
    };

    private readonly Dictionary<string, string?> _savedEnv = new();
    private readonly List<(string Topic, string Payload)> _published = new();
    private readonly RecordingHandler _webhook = new();
    private MqttServer _server = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        foreach (var k in EnvKeys)
        {
            _savedEnv[k] = Environment.GetEnvironmentVariable(k);
            Environment.SetEnvironmentVariable(k, null);
        }

        _port = FreePort();
        _server = new MqttFactory().CreateMqttServer(
            new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build());
        _server.InterceptingPublishAsync += e =>
        {
            var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment);
            lock (_published) _published.Add((e.ApplicationMessage.Topic, payload));
            return Task.CompletedTask;
        };
        await _server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _server.StopAsync();
        _server.Dispose();
        foreach (var (k, v) in _savedEnv) Environment.SetEnvironmentVariable(k, v);
    }

    [Fact]
    public async Task Sim_event_goes_to_its_notify_topic_and_not_to_the_webhook()
    {
        var dispatcher = await CreateDispatcherAsync();

        await dispatcher.DispatchAsync(LowBattery("SIM_Zone1"), forwardSimToNotify: false);

        var msg = await WaitForMessageAsync("xmini-sim/SIM_Zone1/notify");
        var json = JsonDocument.Parse(msg.Payload).RootElement;
        Assert.Equal("SIM_Zone1", json.GetProperty("deviceId").GetString());
        Assert.Equal("battery.low", json.GetProperty("event").GetString());
        Assert.Equal("warning", json.GetProperty("severity").GetString());
        Assert.StartsWith("SIM_Zone1:battery.low:", json.GetProperty("id").GetString());
        Assert.Equal(15, json.GetProperty("data").GetProperty("batteryPercent").GetInt32());
        Assert.Empty(_webhook.Requests);
    }

    [Fact]
    public async Task Sim_event_also_reaches_the_real_webhook_when_forwarding()
    {
        var dispatcher = await CreateDispatcherAsync();

        await dispatcher.DispatchAsync(LowBattery("SIM_Zone1"), forwardSimToNotify: true);

        await WaitForMessageAsync("xmini-sim/SIM_Zone1/notify");
        var request = Assert.Single(_webhook.Requests);
        Assert.Contains("\"event\":\"battery.low\"", request);
    }

    [Fact]
    public async Task Real_device_event_goes_to_the_webhook_and_never_to_mqtt()
    {
        var dispatcher = await CreateDispatcherAsync();

        await dispatcher.DispatchAsync(LowBattery("ESP32S3_Zone1"), forwardSimToNotify: true);

        Assert.Single(_webhook.Requests);
        await Task.Delay(300); // cho broker thời gian nhận nếu (sai) có publish
        lock (_published) Assert.Empty(_published);
    }

    private static NotifyEvent LowBattery(string deviceId)
        => new(deviceId, "battery.low", NotifySeverity.Warning,
            new Dictionary<string, object?> { ["batteryPercent"] = 15 });

    private async Task<NotifyDispatcher> CreateDispatcherAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Broker"] = "127.0.0.1",
                ["Mqtt:Port"] = _port.ToString(),
                ["Mqtt:UseTls"] = "false",
                ["Notify:BaseUrl"] = "http://notify.test",
                ["Notify:ApiKey"] = "test-key",
            })
            .Build();
        var publisher = new MqttPublisherService(NullLogger<MqttPublisherService>.Instance);
        await publisher.InitializeAsync(config);
        var notify = new NotifyClient(new FakeHttpFactory(_webhook), NullLogger<NotifyClient>.Instance, config);
        return new NotifyDispatcher(publisher, notify);
    }

    private async Task<(string Topic, string Payload)> WaitForMessageAsync(string topic, int timeoutMs = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            lock (_published)
            {
                var hit = _published.FirstOrDefault(p => p.Topic == topic);
                if (hit.Topic != null) return hit;
            }
            await Task.Delay(25);
        }
        throw new TimeoutException($"Không thấy message trên {topic} trong {timeoutMs}ms");
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private sealed class FakeHttpFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://notify.test") };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
