# Plant Tree MCP Server

Python MCP server connecting Ollama to Plant Tree IoT REST API.

## Setup on Mac Mini

### 1. Install dependencies

```bash
cd mcp-server
python -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

### 2. Run tests

```bash
pytest tests/ -v
```

Expected: 13 passed

### 3. Start .NET REST API

```bash
# In plant-tree/PlantTreeIoTServer
dotnet run
# API runs on http://localhost:5000
```

### 4. Connect to Ollama

Add to your Ollama MCP config (location depends on your Ollama UI — Open WebUI, Msty, AnythingLLM, etc.):

```json
{
  "mcpServers": {
    "plant-tree": {
      "command": "/absolute/path/to/venv/bin/python",
      "args": ["/absolute/path/to/mcp-server/server.py"]
    }
  }
}
```

Replace `/absolute/path/to/` with the actual path on Mac Mini.

### 5. Test with Ollama

```
>>> Liệt kê tất cả thiết bị IoT đang có
>>> Cây phòng khách có cần tưới không?
>>> Tưới cây 30 giây đi
>>> Đặt rule tự động tưới khi độ ẩm đất < 25%
```

## Startup Order

```
1. Docker (Mosquitto)    → runs automatically
2. dotnet run            → REST API on port 5000
3. ollama serve          → AI model on port 11434
4. MCP server            → starts automatically when Ollama needs it
```

## Tools Available (12 total)

| Group | Tools |
|---|---|
| Devices | `list_devices`, `get_device_info` |
| Sensors | `get_latest_sensor`, `get_sensor_history` |
| Control | `send_command`, `get_pending_commands`, `auto_water`, `auto_light` |
| Rules | `get_moisture_rule`, `set_moisture_rule`, `get_light_rule`, `set_light_rule` |

## Upgrade Path

| Version | Feature |
|---|---|
| v1.1 | API Key auth, direct MQTT commands |
| v2.0 | AI analytics, trend detection, smart rule suggestions |
