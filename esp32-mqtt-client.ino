// ESP32 Plant Tree IoT - MQTT Client (REFERENCE SAMPLE)
//
// Mẫu tham chiếu, bám theo hợp đồng MQTT trong `mqtt-api.md` (mô hình device-native):
// THIẾT BỊ tự chạy auto theo ngưỡng lưu trong NVS. Backend chỉ đọc telemetry, đọc/đặt
// NGƯỠNG (config), và gửi lệnh THỦ CÔNG. KHÔNG còn rule-engine tự tưới phía server
// (không có WATER_ON/LIGHT_ON). Muốn đổi hành vi auto -> chỉnh NGƯỠNG qua `xmini/config`.
//
// Chỉ 3 topic, dùng chung tiền tố `xmini/` (QoS 0, không retained):
//   - `xmini/sensor_data` (Device->BE): telemetry phẳng snake_case (~10s).
//   - `xmini/config`      (Device->BE): 15 ngưỡng auto hiện tại (khi kết nối + sau mỗi lần đổi).
//   - `xmini/control`     (BE->Device): lệnh, dạng 1 JSON object PHẲNG.
//
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

// HiveMQ Cloud credentials (cùng broker với hợp đồng)
const char* MQTT_BROKER   = "ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud";
const int   MQTT_PORT     = 8883;  // TLS port
const char* MQTT_USERNAME = "planttreeiot";
const char* MQTT_PASSWORD = "Test1234!";

const char* DEVICE_ID = "esp32-001";

// Topics của hợp đồng — cố định, KHÔNG nhúng deviceId (deviceId nằm trong payload)
const char* TOPIC_SENSOR_DATA = "xmini/sensor_data";  // Device -> BE (telemetry)
const char* TOPIC_CONFIG      = "xmini/config";       // Device -> BE (ngưỡng auto)
const char* TOPIC_CONTROL     = "xmini/control";      // BE -> Device (lệnh phẳng)

// Sensor / actuator pins
#define DHT_PIN           4
#define SOIL_MOISTURE_PIN 34
#define LIGHT_SENSOR_PIN  35
#define WATER_LEVEL_PIN   32
#define WATER_PUMP_PIN    26
#define LIGHT_PIN         27

// Timing
const unsigned long SENSOR_INTERVAL = 10000;  // ~10s theo hợp đồng
// =======================================

DHT dht(DHT_PIN, DHT11);
WiFiClientSecure wifiClient;
PubSubClient mqttClient(wifiClient);

// Nhịp thử kết nối lại (non-blocking) — board đặt xa, phải tự hồi phục khi mạng chập chờn
const unsigned long MQTT_RETRY_INTERVAL  = 5000;   // thử lại MQTT mỗi 5s
const unsigned long WIFI_CONNECT_TIMEOUT = 20000;  // 1 lượt kết nối WiFi tối đa 20s rồi nhả ra
const unsigned long PUMP_MAX_MS          = 60000;  // chặn thời gian bơm tối đa (an toàn)

// Trạng thái chấp hành hiện tại — báo cáo lại trong telemetry.
// Thiết bị thật tự chạy auto; lệnh pump/light thủ công sẽ ép sang MANUAL.
bool   pumpOn   = false;
bool   lightOn  = false;
int    lightPwm = 0;                 // 0-255
String mode     = "auto";            // "auto" | "manual"

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

  connectWifi();

  wifiClient.setInsecure();  // Bỏ verify chứng chỉ (khớp firmware; OK cho dự án cá nhân)
  mqttClient.setServer(MQTT_BROKER, MQTT_PORT);
  mqttClient.setCallback(onControlReceived);
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
    setPump(false);
    Serial.println("pump OFF (auto sau khi hết thời gian an toàn)");
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
// 1 lần thử kết nối MQTT (non-blocking). Thành công thì subscribe + gửi config hiện tại;
// thất bại thì trả về ngay để loop() thử lại theo nhịp MQTT_RETRY_INTERVAL.
void connectMqtt() {
  if (WiFi.status() != WL_CONNECTED) return;   // cần WiFi trước đã

  String clientId = String("esp32-") + String(DEVICE_ID) + "-" + String(random(0xffff), HEX);

  Serial.print("Connecting to MQTT broker...");
  if (mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD)) {
    Serial.println("Connected!");
    // Subscribe lệnh điều khiển — QoS 0 theo hợp đồng
    mqttClient.subscribe(TOPIC_CONTROL, 0);
    Serial.println("Subscribed to: " + String(TOPIC_CONTROL));
    // Khi vừa kết nối: gửi ngưỡng auto hiện tại lên `xmini/config`
    publishConfig();
  } else {
    Serial.println(" thất bại (rc=" + String(mqttClient.state()) + "), sẽ thử lại");
  }
}

// ============ Actuators ============
void setPump(bool on) {
  pumpOn = on;
  digitalWrite(WATER_PUMP_PIN, on ? HIGH : LOW);
  // Bật bơm thủ công: đặt mốc tự tắt an toàn (non-blocking); tắt thì xoá mốc
  pumpOffAt = on ? (millis() + PUMP_MAX_MS) : 0;
}

void setLight(bool on) {
  lightOn = on;
  analogWrite(LIGHT_PIN, on ? lightPwm : 0);
}

// ============ Publish telemetry -> xmini/sensor_data ============
// Payload PHẲNG snake_case theo hợp đồng. Đây là tập trường tiêu biểu (không đủ 21 trường)
// nhưng CHỈ dùng đúng tên trong hợp đồng. Cảm biến lỗi -> null (riêng battery_percent = -1).
void publishSensorData() {
  float temperature = dht.readTemperature();
  float humidity    = dht.readHumidity();
  int   soilRaw     = analogRead(SOIL_MOISTURE_PIN);
  int   lightRaw    = analogRead(LIGHT_SENSOR_PIN);

  int   soilPercent = map(soilRaw, 4095, 0, 0, 100);    // 0-100, không null
  float lightLux    = map(lightRaw, 0, 4095, 0, 1000);  // ước lượng lux

  StaticJsonDocument<384> doc;
  doc["device_id"] = DEVICE_ID;

  if (!isnan(temperature)) doc["temperature_c"]    = temperature; else doc["temperature_c"]    = nullptr;
  if (!isnan(humidity))    doc["humidity_percent"] = humidity;    else doc["humidity_percent"] = nullptr;

  doc["soil_percent"]   = soilPercent;     // (KHÔNG phải soil_moisture_percent)
  doc["light_lux"]      = lightLux;
  doc["soil_dry_flag"]  = (soilPercent < 30);

  // Trạng thái chấp hành do THIẾT BỊ tự quyết (auto) hoặc do lệnh thủ công
  doc["light_on"]  = lightOn;
  doc["light_pwm"] = lightPwm;
  doc["pump_on"]   = pumpOn;
  doc["mode"]      = mode;          // "auto" | "manual"
  doc["water_ok"]  = true;         // null nếu chưa xác định được

  // Pin (ví dụ giá trị đọc từ INA219; battery_percent = -1 khi lỗi)
  doc["battery_voltage_v"] = 3.9;
  doc["battery_percent"]   = 76;

  char payload[384];
  serializeJson(doc, payload);

  if (mqttClient.publish(TOPIC_SENSOR_DATA, payload)) {
    Serial.println("sensor_data published: " + String(payload));
  } else {
    Serial.println("Publish failed");
  }
}

// ============ Publish ngưỡng auto -> xmini/config ============
// Bọc trong khoá `config`. Tập ngưỡng tiêu biểu (tên đúng hợp đồng); thiết bị thật gửi đủ 15.
void publishConfig() {
  StaticJsonDocument<384> doc;
  JsonObject cfg = doc.createNestedObject("config");
  cfg["soil_on_pct"]    = 30;
  cfg["soil_off_pct"]   = 60;
  cfg["pump_max_run_s"] = 20;
  cfg["pump_cooldown_s"] = 300;
  cfg["lux_on"]         = 120;
  cfg["lux_off"]        = 300;
  cfg["light_auto_pwm"] = 180;
  cfg["batt_warn_pct"]  = 20;

  char payload[384];
  serializeJson(doc, payload);
  mqttClient.publish(TOPIC_CONFIG, payload);
  Serial.println("config published: " + String(payload));
}

// ============ Handle lệnh đến <- xmini/control ============
// Lệnh là 1 JSON object PHẲNG. Chỉ đọc các khoá hợp đồng; khoá lạ bị bỏ qua.
// Nhận pump/light/light_pwm -> ép sang MANUAL. KHÔNG có {"command":...}, không có FAN.
void onControlReceived(char* topic, byte* payload, unsigned int length) {
  String message;
  for (unsigned int i = 0; i < length; i++) {
    message += (char)payload[i];
  }
  Serial.println("Control received: " + message);

  StaticJsonDocument<384> doc;
  if (deserializeJson(doc, message) != DeserializationError::Ok) {
    Serial.println("Invalid JSON");
    return;
  }

  // Chấp hành THỦ CÔNG -> ép MANUAL
  if (doc.containsKey("pump")) {
    mode = "manual";
    setPump(doc["pump"].as<bool>());
    Serial.println("pump " + String(pumpOn ? "ON" : "OFF") + " (manual)");
  }

  if (doc.containsKey("light_pwm")) {
    lightPwm = constrain(doc["light_pwm"].as<int>(), 0, 255);
    if (lightOn) analogWrite(LIGHT_PIN, lightPwm);
    Serial.println("light_pwm = " + String(lightPwm));
  }

  if (doc.containsKey("light")) {
    mode = "manual";
    setLight(doc["light"].as<bool>());
    Serial.println("light " + String(lightOn ? "ON" : "OFF") + " (manual)");
  }

  // Đổi chế độ: {"mode":"auto"|"manual"} hoặc {"auto":true|false}
  if (doc.containsKey("mode")) {
    mode = String((const char*)doc["mode"]);
    Serial.println("mode = " + mode);
  } else if (doc.containsKey("auto")) {
    mode = doc["auto"].as<bool>() ? "auto" : "manual";
    Serial.println("mode = " + mode);
  }

  // Ngưỡng auto: lưu NVS (không đổi mode). {"config":{}} = yêu cầu gửi lại config hiện tại.
  if (doc.containsKey("config")) {
    JsonObject cfg = doc["config"];
    if (cfg.size() == 0) {
      publishConfig();  // yêu cầu re-send
    } else {
      // TODO: ghi từng ngưỡng (soil_on_pct, lux_on, ...) vào NVS rồi publishConfig()
      Serial.println("config updated (lưu NVS)");
    }
  }

  // Màn hình TFT: {"message":"..."} (ASCII, "" để xoá) + tùy chọn {"message_secs":N}
  if (doc.containsKey("message")) {
    const char* text = doc["message"] | "";
    int secs = doc["message_secs"] | 0;
    Serial.println("message: \"" + String(text) + "\" (" + String(secs) + "s)");
  }
}
