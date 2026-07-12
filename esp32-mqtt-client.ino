// ESP32 Plant Tree IoT - MQTT Client
// Libraries needed (Arduino Library Manager):
//   - PubSubClient by Nick O'Leary
//   - ArduinoJson by Benoit Blanchon
//   - DHT sensor library by Adafruit
//
// AUTH: Client này giao tiếp hoàn toàn qua MQTT, xác thực bằng credential của
// broker HiveMQ (MQTT_USERNAME/MQTT_PASSWORD bên dưới). Không dùng HTTP API nên
// KHÔNG cần device token (X-Device-Id/X-Device-Secret).
//
// HiveMQ Cloud nằm trên internet nên board có thể đặt ở bất kỳ đâu có WiFi
// (không cần cùng mạng với server). WiFi/MQTT tự kết nối lại khi rớt.

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <DHT.h>

// ============ CONFIGURATION ============
const char* WIFI_SSID     = "your-wifi-ssid";
const char* WIFI_PASSWORD = "your-wifi-password";

// HiveMQ Cloud credentials
const char* MQTT_BROKER   = "ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud";
const int   MQTT_PORT     = 8883;  // TLS port
const char* MQTT_USERNAME = "nod-iot-plant";
const char* MQTT_PASSWORD = "Nod-iot-plant1234";

const char* DEVICE_ID = "esp32-001";

// Sensor pins
#define DHT_PIN          4
#define SOIL_MOISTURE_PIN 34
#define LIGHT_SENSOR_PIN  35
#define WATER_LEVEL_PIN   32
#define WATER_PUMP_PIN    26
#define LIGHT_PIN         27

// Timing
const unsigned long SENSOR_INTERVAL = 30000;  // 30 seconds
// =======================================

DHT dht(DHT_PIN, DHT11);
WiFiClientSecure wifiClient;
PubSubClient mqttClient(wifiClient);

// Nhịp thử kết nối lại (non-blocking) — board đặt xa, phải tự hồi phục khi mạng chập chờn
const unsigned long MQTT_RETRY_INTERVAL  = 5000;   // thử lại MQTT mỗi 5s
const unsigned long WIFI_CONNECT_TIMEOUT = 20000;  // 1 lượt kết nối WiFi tối đa 20s rồi nhả ra
const unsigned long PUMP_MAX_MS          = 60000;  // chặn thời gian bơm tối đa (an toàn)

char sensorsTopic[64];
char commandsTopic[64];
unsigned long lastSensorPublish = 0;
unsigned long lastMqttRetry     = 0;
unsigned long pumpOffAt         = 0;  // != 0: mốc millis() sẽ tự tắt bơm (non-blocking)

void setup() {
  Serial.begin(115200);
  pinMode(WATER_PUMP_PIN, OUTPUT);
  pinMode(LIGHT_PIN, OUTPUT);
  digitalWrite(WATER_PUMP_PIN, LOW);
  digitalWrite(LIGHT_PIN, LOW);

  dht.begin();

  // Build topics
  snprintf(sensorsTopic,  sizeof(sensorsTopic),  "planttree/%s/sensors",  DEVICE_ID);
  snprintf(commandsTopic, sizeof(commandsTopic), "planttree/%s/commands", DEVICE_ID);

  connectWifi();

  wifiClient.setInsecure();  // Skip certificate verification (OK for personal projects)
  mqttClient.setServer(MQTT_BROKER, MQTT_PORT);
  mqttClient.setCallback(onCommandReceived);
  mqttClient.setBufferSize(512);

  connectMqtt();
}

void loop() {
  // 1) Giữ WiFi sống — nếu rớt thì tự kết nối lại (board ở xa, không reset tay được)
  if (WiFi.status() != WL_CONNECTED) {
    ensureWifi();   // thử tối đa WIFI_CONNECT_TIMEOUT ms rồi nhả ra
    return;         // chưa có WiFi -> bỏ qua vòng này, thử lại vòng sau
  }

  // 2) Giữ MQTT sống — thử lại có nhịp (non-blocking), KHÔNG kẹt vòng lặp
  if (!mqttClient.connected()) {
    unsigned long now = millis();
    if (now - lastMqttRetry >= MQTT_RETRY_INTERVAL) {
      lastMqttRetry = now;
      connectMqtt();  // 1 lần thử, thành/bại đều trả về ngay
    }
    return;           // chưa có MQTT -> chưa publish
  }

  mqttClient.loop();

  // Tự tắt bơm khi hết thời gian — non-blocking, MQTT vẫn được phục vụ liên tục
  if (pumpOffAt != 0 && millis() >= pumpOffAt) {
    digitalWrite(WATER_PUMP_PIN, LOW);
    pumpOffAt = 0;
    Serial.println("WATER_OFF (auto sau khi hết thời gian)");
  }

  unsigned long now = millis();
  if (now - lastSensorPublish >= SENSOR_INTERVAL) {
    publishSensorData();
    lastSensorPublish = now;
  }
}

// ============ WiFi ============
void connectWifi() {
  WiFi.mode(WIFI_STA);
  WiFi.setAutoReconnect(true);   // ESP32 tự kết nối lại khi rớt
  WiFi.persistent(true);
  ensureWifi();
}

// Thử kết nối WiFi tối đa WIFI_CONNECT_TIMEOUT ms rồi trả về (không kẹt vĩnh viễn).
// Trả true nếu đã kết nối.
bool ensureWifi() {
  if (WiFi.status() == WL_CONNECTED) return true;

  Serial.print("Connecting to WiFi");
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  unsigned long start = millis();
  while (WiFi.status() != WL_CONNECTED && millis() - start < WIFI_CONNECT_TIMEOUT) {
    delay(500);
    Serial.print(".");
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.println("\nWiFi connected: " + WiFi.localIP().toString());
    return true;
  }
  Serial.println("\nWiFi chưa kết nối được, sẽ thử lại...");
  return false;
}

// ============ MQTT ============
// 1 lần thử kết nối MQTT (non-blocking). Thành công thì subscribe; thất bại thì
// trả về ngay để loop() thử lại theo nhịp MQTT_RETRY_INTERVAL.
void connectMqtt() {
  if (WiFi.status() != WL_CONNECTED) return;   // cần WiFi trước đã

  String clientId = String("esp32-") + String(DEVICE_ID) + "-" + String(random(0xffff), HEX);

  Serial.print("Connecting to MQTT broker...");
  if (mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD)) {
    Serial.println("Connected!");
    // Subscribe to commands topic
    mqttClient.subscribe(commandsTopic, 1);
    Serial.println("Subscribed to: " + String(commandsTopic));
  } else {
    Serial.println(" thất bại (rc=" + String(mqttClient.state()) + "), sẽ thử lại");
  }
}

// ============ Publish sensor data ============
void publishSensorData() {
  float temperature   = dht.readTemperature();
  float humidity      = dht.readHumidity();
  int soilRaw         = analogRead(SOIL_MOISTURE_PIN);
  int lightRaw        = analogRead(LIGHT_SENSOR_PIN);
  int waterRaw        = analogRead(WATER_LEVEL_PIN);

  float soilMoisture = map(soilRaw, 4095, 0, 0, 100);  // Adjust for your sensor
  float lightLevel   = map(lightRaw, 0, 4095, 0, 100);
  float waterLevel   = map(waterRaw, 0, 4095, 0, 100);

  StaticJsonDocument<256> doc;
  if (!isnan(temperature)) doc["temperature"]  = temperature;
  if (!isnan(humidity))    doc["humidity"]      = humidity;
  doc["soilMoisture"] = soilMoisture;
  doc["lightLevel"]   = lightLevel;
  doc["waterLevel"]   = waterLevel;

  char payload[256];
  serializeJson(doc, payload);

  if (mqttClient.publish(sensorsTopic, payload)) {
    Serial.println("Sensors published: " + String(payload));
  } else {
    Serial.println("Publish failed");
  }
}

// ============ Handle incoming commands ============
void onCommandReceived(char* topic, byte* payload, unsigned int length) {
  String message;
  for (unsigned int i = 0; i < length; i++) {
    message += (char)payload[i];
  }
  Serial.println("Command received: " + message);

  StaticJsonDocument<256> doc;
  if (deserializeJson(doc, message) != DeserializationError::Ok) {
    Serial.println("Invalid JSON");
    return;
  }

  const char* command = doc["command"];

  if (strcmp(command, "WATER_ON") == 0) {
    // Clamp duration để lệnh lỗi/độc hại không treo board; bơm tự tắt trong loop() (non-blocking)
    long duration = doc["parameters"]["duration"] | 5000;
    if (duration < 0) duration = 0;
    if (duration > (long)PUMP_MAX_MS) duration = PUMP_MAX_MS;
    Serial.println("WATER_ON for " + String(duration) + "ms");
    digitalWrite(WATER_PUMP_PIN, HIGH);
    pumpOffAt = millis() + (unsigned long)duration;

  } else if (strcmp(command, "WATER_OFF") == 0) {
    digitalWrite(WATER_PUMP_PIN, LOW);
    pumpOffAt = 0;
    Serial.println("WATER_OFF");

  } else if (strcmp(command, "LIGHT_ON") == 0) {
    digitalWrite(LIGHT_PIN, HIGH);
    Serial.println("LIGHT_ON");

  } else if (strcmp(command, "LIGHT_OFF") == 0) {
    digitalWrite(LIGHT_PIN, LOW);
    Serial.println("LIGHT_OFF");
  }
}
