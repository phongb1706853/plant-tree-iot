using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;
using PlantTreeIoTServer.Services;
using Xunit;

namespace PlantTreeIoTServer.Tests;

// Kiểm thử tích hợp publisher với broker MQTTnet chạy NGAY TRONG tiến trình test (không ra mạng).
//
// Bối cảnh (review #3): HiveMQ NGẮT kết nối client MQTT 3.1.1 publish lên topic không được phép. Nếu credential
// của server thiếu quyền xmini-sim/#, lệnh đầu tiên cho thiết bị ảo làm publisher rớt — và trước đây publisher
// không bao giờ nối lại (cờ _isConnected vẫn true) -> mọi lệnh sau đó, kể cả cho ESP32 THẬT, đều hỏng tới khi
// restart server. Broker giả dưới đây đá client khi publish lên xmini-sim/ để tái hiện đúng tình huống đó.
[Collection("MqttEnv")]
public class MqttPublisherReconnectTests : IAsyncLifetime
{
    private static readonly string[] EnvKeys =
        { "MQTT_BROKER", "MQTT_PORT", "MQTT_USERNAME", "MQTT_PASSWORD", "MQTT_USE_TLS", "MQTT_ALLOW_INVALID_CERT" };

    private readonly Dictionary<string, string?> _savedEnv = new();
    private readonly List<string> _deliveredTopics = new();
    private readonly List<(string Topic, MqttQualityOfServiceLevel Qos, bool Retain, string Payload)> _delivered = new();
    private MqttServer _server = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        // Env có ưu tiên hơn config trong MqttPublisherService -> xoá để test KHÔNG BAO GIỜ nối broker thật.
        foreach (var k in EnvKeys)
        {
            _savedEnv[k] = Environment.GetEnvironmentVariable(k);
            Environment.SetEnvironmentVariable(k, null);
        }

        _port = FreePort();
        var options = new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointPort(_port).Build();
        _server = new MqttFactory().CreateMqttServer(options);
        _server.InterceptingPublishAsync += e =>
        {
            var topic = e.ApplicationMessage.Topic;
            if (topic.StartsWith(SimTopics.Prefix + "/", StringComparison.Ordinal))
            {
                e.ProcessPublish = false; // như HiveMQ: không có quyền -> bỏ tin + ngắt client
                var clientId = e.ClientId;
                _ = Task.Run(() => _server.DisconnectClientAsync(clientId, MqttDisconnectReasonCode.NotAuthorized));
            }
            else
            {
                var msg = e.ApplicationMessage;
                lock (_deliveredTopics)
                {
                    _deliveredTopics.Add(topic);
                    _delivered.Add((topic, msg.QualityOfServiceLevel, msg.Retain,
                        System.Text.Encoding.UTF8.GetString(msg.PayloadSegment)));
                }
            }
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
    public async Task Publisher_reconnects_after_the_broker_drops_it()
    {
        var publisher = await CreatePublisherAsync();
        Assert.True(publisher.IsConnected);

        await publisher.PublishControlAsync("SIM_Zone1", new Dictionary<string, object?> { ["pump"] = true });
        await WaitUntilAsync(() => !publisher.IsConnected); // broker đã đá publisher

        var ok = await publisher.PublishControlAsync("ESP32S3_Zone1", new Dictionary<string, object?> { ["pump"] = true });

        Assert.True(ok);
        await WaitUntilAsync(() => { lock (_deliveredTopics) return _deliveredTopics.Contains(SimTopics.RealControlTopic); });
    }

    [Fact]
    public async Task Control_goes_out_with_qos1_not_retained_and_an_id()
    {
        // Firmware 2.1.0: thiết bị subscribe QoS 1 + persistent session; QoS 0 lúc thiết bị ngủ sẽ mất, lệnh retained
        // bị giao lại mỗi lần nối, và mỗi lệnh cần "id" để chống giao lặp (backend-api.md mục 5.1, 5.4).
        var publisher = await CreatePublisherAsync();
        var flat = new Dictionary<string, object?> { ["pump"] = true };

        Assert.True(await publisher.PublishControlAsync("ESP32S3_Zone1", flat));

        await WaitUntilAsync(() => { lock (_deliveredTopics) return _delivered.Count > 0; });
        var (topic, qos, retain, payload) = _delivered[0];
        Assert.Equal(SimTopics.RealControlTopic, topic);
        Assert.Equal(MqttQualityOfServiceLevel.AtLeastOnce, qos);
        Assert.False(retain);
        Assert.Contains("\"id\":\"c-", payload);
        Assert.StartsWith("c-", (string)flat["id"]!); // id được ghi lại vào dict để caller log
    }

    [Fact]
    public async Task Existing_id_is_kept()
    {
        var publisher = await CreatePublisherAsync();
        var flat = new Dictionary<string, object?> { ["id"] = "c-1-1", ["light"] = false };

        Assert.True(await publisher.PublishControlAsync("ESP32S3_Zone1", flat));

        await WaitUntilAsync(() => { lock (_deliveredTopics) return _delivered.Count > 0; });
        Assert.Contains("\"id\":\"c-1-1\"", _delivered[0].Payload);
    }

    [Fact]
    public async Task IsConnected_reflects_a_dropped_connection()
    {
        var publisher = await CreatePublisherAsync();

        await publisher.PublishControlAsync("SIM_Zone1", new Dictionary<string, object?> { ["light"] = true });

        await WaitUntilAsync(() => !publisher.IsConnected);
    }

    private async Task<MqttPublisherService> CreatePublisherAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Mqtt:Broker"] = "127.0.0.1",
                ["Mqtt:Port"] = _port.ToString(),
                ["Mqtt:UseTls"] = "false",
            })
            .Build();
        var publisher = new MqttPublisherService(NullLogger<MqttPublisherService>.Instance);
        await publisher.InitializeAsync(config);
        return publisher;
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Điều kiện không đạt trong " + timeoutMs + "ms");
            await Task.Delay(25);
        }
    }
}
