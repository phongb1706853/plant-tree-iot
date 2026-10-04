using Xunit;

namespace PlantTreeIoTServer.Tests;

// Các test dựng broker MQTT trong tiến trình phải xoá biến môi trường MQTT_*/NOTIFY_* (env thắng config trong
// service). Gom vào 1 collection KHÔNG chạy song song để chúng không giẫm env của nhau.
[CollectionDefinition("MqttEnv", DisableParallelization = true)]
public class MqttEnvCollection { }
