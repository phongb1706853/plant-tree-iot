// ESP32 Plant Tree IoT Client for Azure Deployment
// Update SERVER_URL with your Azure deployment URL

#include <WiFi.h>
#include <HTTPClient.h>
#include <ArduinoJson.h>
#include <DHT.h>

// WiFi credentials
const char* WIFI_SSID = "your-wifi-ssid";
const char* WIFI_PASSWORD = "your-wifi-password";

// Azure server URL (update with your deployment)
const char* SERVER_URL = "http://your-azure-container-url"; // e.g., "http://planttree-iot-server.eastus.azurecontainer.io"

// Device configuration
const char* DEVICE_ID = "ESP32_001";

// Sensor pins
#define DHT_PIN 4
#define SOIL_MOISTURE_PIN 34
#define LIGHT_SENSOR_PIN 35
#define WATER_LEVEL_PIN 32

// Sensor objects
DHT dht(DHT_PIN, DHT11);

// Timing
unsigned long lastUpload = 0;
const unsigned long UPLOAD_INTERVAL = 30000; // 30 seconds
unsigned long lastHeartbeat = 0;
const unsigned long HEARTBEAT_INTERVAL = 60000; // 1 minute

void setup() {
  Serial.begin(115200);
  dht.begin();

  // Connect to WiFi
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);
  Serial.print("Connecting to WiFi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWiFi connected!");
  Serial.print("IP Address: ");
  Serial.println(WiFi.localIP());

  // Register device on first boot
  registerDevice();
}

void loop() {
  unsigned long currentMillis = millis();

  // Upload sensor data periodically
  if (currentMillis - lastUpload >= UPLOAD_INTERVAL) {
    uploadSensorData();
    lastUpload = currentMillis;
  }

  // Send heartbeat periodically
  if (currentMillis - lastHeartbeat >= HEARTBEAT_INTERVAL) {
    sendHeartbeat();
    lastHeartbeat = currentMillis;
  }

  // Check for pending commands
  checkPendingCommands();

  delay(1000);
}

void registerDevice() {
  if (WiFi.status() != WL_CONNECTED) return;

  HTTPClient http;
  http.begin(String(SERVER_URL) + "/api/devices/register");
  http.addHeader("Content-Type", "application/json");

  DynamicJsonDocument doc(256);
  doc["deviceId"] = DEVICE_ID;
  doc["name"] = "ESP32 Plant Monitor";
  doc["location"] = "Garden";
  doc["plantType"] = "Pine Tree";

  String jsonString;
  serializeJson(doc, jsonString);

  Serial.println("Registering device...");
  int httpResponseCode = http.POST(jsonString);

  if (httpResponseCode > 0) {
    String response = http.getString();
    Serial.println("Device registered successfully");
    Serial.println("Response: " + response);
  } else {
    Serial.println("Error registering device: " + String(httpResponseCode));
  }

  http.end();
}

void uploadSensorData() {
  if (WiFi.status() != WL_CONNECTED) return;

  // Read sensors
  float temperature = dht.readTemperature();
  float humidity = dht.readHumidity();
  int soilMoistureRaw = analogRead(SOIL_MOISTURE_PIN);
  int lightLevelRaw = analogRead(LIGHT_SENSOR_PIN);
  int waterLevelRaw = analogRead(WATER_LEVEL_PIN);

  // Convert readings (adjust these formulas based on your sensors)
  float soilMoisture = map(soilMoistureRaw, 0, 4095, 100, 0); // Percentage
  float lightLevel = map(lightLevelRaw, 0, 4095, 0, 100);     // Percentage
  float waterLevel = map(waterLevelRaw, 0, 4095, 0, 100);     // Percentage

  HTTPClient http;
  http.begin(String(SERVER_URL) + "/api/sensordata/upload");
  http.addHeader("Content-Type", "application/json");

  DynamicJsonDocument doc(512);
  doc["deviceId"] = DEVICE_ID;
  doc["temperature"] = isnan(temperature) ? nullptr : temperature;
  doc["humidity"] = isnan(humidity) ? nullptr : humidity;
  doc["soilMoisture"] = soilMoisture;
  doc["lightLevel"] = lightLevel;
  doc["waterLevel"] = waterLevel;

  String jsonString;
  serializeJson(doc, jsonString);

  Serial.println("Uploading sensor data...");
  int httpResponseCode = http.POST(jsonString);

  if (httpResponseCode > 0) {
    String response = http.getString();
    Serial.println("Data uploaded successfully");
    Serial.println("Response: " + response);
  } else {
    Serial.println("Error uploading data: " + String(httpResponseCode));
  }

  http.end();
}

void sendHeartbeat() {
  if (WiFi.status() != WL_CONNECTED) return;

  HTTPClient http;
  http.begin(String(SERVER_URL) + "/api/devices/" + String(DEVICE_ID) + "/heartbeat");

  Serial.println("Sending heartbeat...");
  int httpResponseCode = http.POST("");

  if (httpResponseCode > 0) {
    Serial.println("Heartbeat sent successfully");
  } else {
    Serial.println("Error sending heartbeat: " + String(httpResponseCode));
  }

  http.end();
}

void checkPendingCommands() {
  if (WiFi.status() != WL_CONNECTED) return;

  HTTPClient http;
  http.begin(String(SERVER_URL) + "/api/control/commands/" + String(DEVICE_ID));

  Serial.println("Checking for pending commands...");
  int httpResponseCode = http.GET();

  if (httpResponseCode == 200) {
    String payload = http.getString();
    Serial.println("Commands received: " + payload);

    // Parse and execute commands
    DynamicJsonDocument doc(1024);
    DeserializationError error = deserializeJson(doc, payload);

    if (!error) {
      JsonArray commands = doc.as<JsonArray>();
      for (JsonObject command : commands) {
        executeCommand(command);
      }
    }
  } else if (httpResponseCode != 404) { // 404 is normal when no commands
    Serial.println("Error checking commands: " + String(httpResponseCode));
  }

  http.end();
}

void executeCommand(JsonObject command) {
  String commandType = command["command"];
  String commandId = command["_id"];

  Serial.println("Executing command: " + commandType);

  // Execute based on command type
  if (commandType == "WATER_ON") {
    // Turn on water pump
    digitalWrite(WATER_PUMP_PIN, HIGH);
    Serial.println("Water pump ON");

    // Mark command as executed
    markCommandExecuted(commandId);

  } else if (commandType == "WATER_OFF") {
    // Turn off water pump
    digitalWrite(WATER_PUMP_PIN, LOW);
    Serial.println("Water pump OFF");

    // Mark command as executed
    markCommandExecuted(commandId);

  } else if (commandType == "LIGHT_ON") {
    // Turn on lights
    digitalWrite(LIGHT_PIN, HIGH);
    Serial.println("Lights ON");

    // Mark command as executed
    markCommandExecuted(commandId);

  } else if (commandType == "LIGHT_OFF") {
    // Turn off lights
    digitalWrite(LIGHT_PIN, LOW);
    Serial.println("Lights OFF");

    // Mark command as executed
    markCommandExecuted(commandId);
  }
}

void markCommandExecuted(String commandId) {
  if (WiFi.status() != WL_CONNECTED) return;

  HTTPClient http;
  http.begin(String(SERVER_URL) + "/api/control/commands/" + commandId + "/executed");

  int httpResponseCode = http.POST("");

  if (httpResponseCode == 200) {
    Serial.println("Command marked as executed");
  } else {
    Serial.println("Error marking command as executed: " + String(httpResponseCode));
  }

  http.end();
}