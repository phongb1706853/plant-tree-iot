// ESP32 Plant Tree IoT - MQTT Client
// Libraries needed (Arduino Library Manager):
//   - PubSubClient by Nick O'Leary
//   - ArduinoJson by Benoit Blanchon
//   - DHT sensor library by Adafruit

#include <WiFi.h>
#include <WiFiClientSecure.h>
#include <PubSubClient.h>
#include <ArduinoJson.h>
#include <DHT.h>

// ============ CONFIGURATION ============
const char* WIFI_SSID     = "your-wifi-ssid";
const char* WIFI_PASSWORD = "your-wifi-password";

// HiveMQ Cloud credentials
const char* MQTT_BROKER   = "your-cluster.s1.eu.hivemq.cloud";
const int   MQTT_PORT     = 8883;  // TLS port
const char* MQTT_USERNAME = "your-username";
const char* MQTT_PASSWORD = "your-password";

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

char sensorsTopic[64];
char commandsTopic[64];
unsigned long lastSensorPublish = 0;

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
  if (!mqttClient.connected()) {
    connectMqtt();
  }
  mqttClient.loop();

  unsigned long now = millis();
  if (now - lastSensorPublish >= SENSOR_INTERVAL) {
    publishSensorData();
    lastSensorPublish = now;
  }
}

// ============ WiFi ============
void connectWifi() {
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  Serial.print("Connecting to WiFi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWiFi connected: " + WiFi.localIP().toString());
}

// ============ MQTT ============
void connectMqtt() {
  String clientId = String("esp32-") + String(DEVICE_ID) + "-" + String(random(0xffff), HEX);

  Serial.print("Connecting to MQTT broker...");
  while (!mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD)) {
    Serial.print(".");
    delay(3000);
  }
  Serial.println("Connected!");

  // Subscribe to commands topic
  mqttClient.subscribe(commandsTopic, 1);
  Serial.println("Subscribed to: " + String(commandsTopic));
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
    int duration = doc["parameters"]["duration"] | 5000;
    Serial.println("WATER_ON for " + String(duration) + "ms");
    digitalWrite(WATER_PUMP_PIN, HIGH);
    delay(duration);
    digitalWrite(WATER_PUMP_PIN, LOW);
    Serial.println("WATER_OFF (auto after duration)");

  } else if (strcmp(command, "WATER_OFF") == 0) {
    digitalWrite(WATER_PUMP_PIN, LOW);
    Serial.println("WATER_OFF");

  } else if (strcmp(command, "LIGHT_ON") == 0) {
    digitalWrite(LIGHT_PIN, HIGH);
    Serial.println("LIGHT_ON");

  } else if (strcmp(command, "LIGHT_OFF") == 0) {
    digitalWrite(LIGHT_PIN, LOW);
    Serial.println("LIGHT_OFF");
  }
}
