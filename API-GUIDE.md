# Plant Tree IoT — API & MQTT Guide

## System Architecture

```
IoT Device (xmini)
  ↓ (Sensor Data)
MQTT: xmini/sensor_data
  ↓
Server (MCP/Rule Engine)
  ↓ (Rule Evaluation)
Triggered Commands
  ↓ (Publish)
MQTT: xmini/control
  ↓
Device receives & executes command
```

---

## 🔐 Authentication

HTTP API yêu cầu xác thực: **JWT** cho người dùng, **Device Token** cho ESP32. Kênh MQTT xác thực riêng bằng credential broker HiveMQ (không đổi).

### Người dùng (JWT)

```
POST /api/auth/register   { "email", "password", "displayName" }   -> { token }
POST /api/auth/login      { "email", "password" }                  -> { token }
```

Gắn `Authorization: Bearer <token>` cho: devices, sensordata (đọc), rules, control (gửi lệnh / auto). Mỗi user chỉ thấy device mình sở hữu.

### Thiết bị ESP32 (Device Token)

User đăng ký device (JWT) → nhận `deviceSecret` **1 lần**. ESP32 gửi header:

```
X-Device-Id: <deviceId>
X-Device-Secret: <deviceSecret>
```

cho: `POST /api/sensordata/upload`, `GET /api/control/commands/{id}`, `POST /api/control/commands/{id}/executed`, `POST /api/devices/{id}/heartbeat`.

> Các ví dụ `curl` bên dưới cần thêm header token tương ứng. Endpoint `GET /api/control/commands/{id}` chấp nhận **cả hai** (ESP32 dùng Device Token, người dùng/MCP dùng Bearer).

---

## MQTT Configuration

**Broker:** HiveCloud (HiveMQ Cloud)
- **Host:** `ba4fbc53bce842ffb0fcd51178d78414.s1.eu.hivemq.cloud`
- **Port:** `8883` (TLS)
- **Username:** `nod-iot-plant`
- **Password:** `Nod-iot-plant1234`

---

## MQTT Topics

| Topic | Direction | Source | Description |
|-------|-----------|--------|-------------|
| `xmini/sensor_data` | Device → Server | IoT Device | Sensor readings (temperature, humidity, light, moisture) |
| `xmini/control` | Server → Device | Server | Control commands (WATER_ON, WATER_OFF, LIGHT_ON, LIGHT_OFF) |

---

## Payload Formats

### 1. Sensor Data (Device → Server via MQTT)

**Topic:** `xmini/sensor_data`

**Payload (JSON):**
```json
{
  "device_id": "ESP32S3_Zone1",
  "temperature_c": 28,
  "humidity_percent": 65,
  "pressure_hpa": 1014.28,
  "altitude_m": 38.2,
  "light_lux": 57.5,
  "soil_moisture_percent": 45.5,
  "soil_moisture_raw": 2540,
  "relay_on": false
}
```

**Also supports HTTP POST:**
```
POST /api/sensordata/upload
Content-Type: application/json

{
  "device_id": "ESP32S3_Zone1",
  "temperature_c": 28,
  "humidity_percent": 65,
  "light_lux": 57.5,
  "soil_moisture_percent": 45.5,
  "soil_moisture_raw": 2540
}
```

**Response:**
```json
{
  "message": "Data uploaded successfully",
  "timestamp": "2026-06-21T10:30:00Z",
  "triggeredCommands": [
    {
      "commandId": "6a378e4dce70376cdac2f38a",
      "command": "WATER_ON",
      "parameters": {
        "duration": 5000,
        "reason": "moisture_rule",
        "ruleId": "rule-123",
        "ruleName": "Auto Water",
        "threshold": 30,
        "currentMoisture": 25
      }
    }
  ]
}
```

---

### 2. Control Commands (Server → Device)

**Via MQTT Topic:** `xmini/control`

**Payload Format:**
```json
{
  "command": "WATER_ON",
  "parameters": {
    "duration": 5000,
    "reason": "moisture_rule",
    "ruleId": "rule-123",
    "currentMoisture": 25
  }
}
```

**Supported Commands:**
- `WATER_ON` — Bật máy bơm nước
- `WATER_OFF` — Tắt máy bơm nước
- `LIGHT_ON` — Bật đèn
- `LIGHT_OFF` — Tắt đèn

**Via HTTP API:**
```
POST /api/control/commands
Content-Type: application/json

{
  "deviceId": "ESP32S3_Zone1",
  "command": "WATER_ON",
  "parameters": {
    "duration": 5000
  }
}
```

**Response:**
```json
{
  "message": "Command sent successfully",
  "commandId": "cmd-uuid-123"
}
```

---

### 3. Moisture Rules

**Create Rule:**
```
POST /api/rules/moisture
Content-Type: application/json

{
  "deviceId": "ESP32S3_Zone1",
  "name": "Auto Water",
  "minMoisture": 30,
  "maxMoisture": 70,
  "waterDurationMs": 5000,
  "cooldownMs": 1800000,
  "isEnabled": true
}
```

**Response:**
```json
{
  "id": "rule-123",
  "deviceId": "ESP32S3_Zone1",
  "name": "Auto Water",
  "minMoisture": 30,
  "maxMoisture": 70,
  "waterDurationMs": 5000,
  "cooldownMs": 1800000,
  "isEnabled": true,
  "createdAt": "2026-06-21T10:00:00Z",
  "lastTriggeredAt": null
}
```

**Get Rules:**
```
GET /api/rules/moisture/ESP32S3_Zone1
```

---

### 4. Light Rules

**Create Rule:**
```
POST /api/rules/light
Content-Type: application/json

{
  "deviceId": "ESP32S3_Zone1",
  "name": "Auto Light",
  "minLight": 25,
  "maxLight": 60,
  "isEnabled": true,
  "cooldownMs": 600000
}
```

**Response:**
```json
{
  "id": "rule-456",
  "deviceId": "ESP32S3_Zone1",
  "name": "Auto Light",
  "minLight": 25,
  "maxLight": 60,
  "cooldownMs": 600000,
  "isEnabled": true,
  "createdAt": "2026-06-21T10:00:00Z",
  "lastTriggeredAt": null
}
```

---

## Rule Evaluation Flow

### When Sensor Data Arrives:

1. **Server receives** sensor data (MQTT or HTTP)
2. **Saves to MongoDB**
3. **Evaluates Moisture Rules:**
   - If `moisture < minMoisture` → Command: `WATER_ON`
   - If `moisture >= maxMoisture` → Command: `WATER_OFF`
4. **Evaluates Light Rules:**
   - If `light < minLight` → Command: `LIGHT_ON`
   - If `light >= maxLight` → Command: `LIGHT_OFF`
5. **Checks Cooldown:** Prevents same rule from triggering within cooldown period
6. **Publishes Commands:**
   - Save to MongoDB
   - Publish to MQTT topic `xmini/control`
7. **Returns Response** with `triggeredCommands`

---

## Cooldown Explained

**Cooldown** = Time window before same rule can trigger again

**Example:**
- Rule: "Water when moisture < 30%"
- Cooldown: 30 minutes
- 10:00 → Moisture 25% → Rule triggers → Water ON ✓
- 10:05 → Moisture 25% → Rule BLOCKED (cooldown active) ✗
- 10:30 → Moisture 25% → Cooldown expired → Rule triggers → Water ON ✓

**Why?** Prevent rapid on/off cycling. Device needs time to respond.

---

## Device Commands

### Polling Approach (HTTP)
Device periodically polls for pending commands:
```
GET /api/control/commands/{deviceId}

Response:
[
  {
    "id": "cmd-123",
    "command": "WATER_ON",
    "parameters": { "duration": 5000 },
    "createdAt": "2026-06-21T10:30:00Z"
  }
]
```

After executing:
```
POST /api/control/commands/{commandId}/executed
```

### MQTT Subscription Approach
Device subscribes to `xmini/control` and receives commands in real-time.

---

## Testing with Interactive Dashboard

### Access Dashboard
```
file:///c:/Work/my-project/plant-tree-iot/interactive-api-dashboard.html
```

### Configure Connection
- **Host:** localhost
- **Port:** 8000
- **Device ID:** ESP32S3_Zone1 (in header)

### Test Flow
1. **Devices** → Register device
2. **Sensors** → Upload sensor data
3. **Rules** → Create moisture/light rules
4. **Control** → Send commands (publishes to MQTT)
5. Check MQTT subscription for received commands

---

## Curl Examples

### Upload Sensor Data
```bash
curl -X POST http://localhost:8000/api/sensordata/upload \
  -H "Content-Type: application/json" \
  -d '{
    "device_id": "ESP32S3_Zone1",
    "temperature_c": 28,
    "humidity_percent": 65,
    "light_lux": 57.5,
    "soil_moisture_percent": 45.5,
    "soil_moisture_raw": 2540
  }'
```

### Create Moisture Rule
```bash
curl -X POST http://localhost:8000/api/rules/moisture \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "ESP32S3_Zone1",
    "name": "Auto Water",
    "minMoisture": 30,
    "maxMoisture": 70,
    "waterDurationMs": 5000,
    "cooldownMs": 1800000
  }'
```

### Send Command via API
```bash
curl -X POST http://localhost:8000/api/control/commands \
  -H "Content-Type: application/json" \
  -d '{
    "deviceId": "ESP32S3_Zone1",
    "command": "WATER_ON",
    "parameters": {
      "duration": 5000
    }
  }'
```

---

## Database Schema

### SensorData
```javascript
{
  _id: ObjectId,
  deviceId: "ESP32S3_Zone1",
  timestamp: ISODate,
  temperature: 28,
  humidity: 65,
  soilMoisture: 45.5,
  lightLevel: 57.5,
  waterLevel: null,
  phLevel: null,
  pressure: 1014.28,
  altitude: 38.2,
  soilMoistureRaw: 2540,
  relayOn: false,
  location: null
}
```

### MoistureRules
```javascript
{
  _id: ObjectId,
  deviceId: "ESP32S3_Zone1",
  name: "Auto Water",
  minMoisture: 30,
  maxMoisture: 70,
  waterDurationMs: 5000,
  cooldownMs: 1800000,
  isEnabled: true,
  createdAt: ISODate,
  lastTriggeredAt: ISODate
}
```

### ControlCommands
```javascript
{
  _id: ObjectId,
  deviceId: "ESP32S3_Zone1",
  command: "WATER_ON",
  parameters: {
    duration: 5000,
    reason: "moisture_rule",
    ruleId: "rule-123"
  },
  executed: false,
  executedAt: null,
  createdAt: ISODate
}
```

---

## Troubleshooting

### Rules Not Triggering

1. **Check rule exists:**
   ```
   GET /api/rules/moisture/ESP32S3_Zone1
   ```

2. **Check sensor values match rule conditions:**
   - Rule: `minMoisture: 30`
   - Sensor: `soil_moisture_percent: 45.5`
   - Expected: No trigger (45.5 is between 30-70)

3. **Check cooldown:**
   - `lastTriggeredAt` + `cooldownMs` must pass

4. **Check rule is enabled:**
   - `"isEnabled": true`

### Commands Not Reaching Device

1. **MQTT Connected?**
   - Check server logs for: `Connected to MQTT broker`

2. **Device Subscribed?**
   - Verify device subscribes to `xmini/control`

3. **Topic Name Correct?**
   - xmini devices: `xmini/control`
   - Other devices: `planttree/{deviceId}/commands`

---

## Next Steps

- Configure device to subscribe MQTT topic
- Set up rules based on plant requirements
- Monitor sensor data and adjust thresholds
- Test end-to-end flow with real hardware
