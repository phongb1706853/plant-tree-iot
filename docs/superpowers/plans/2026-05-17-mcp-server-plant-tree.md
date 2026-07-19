> ⚠️ **SUPERSEDED (2026-07-15).** Tài liệu lịch sử — mô tả thiết kế cũ. Hợp đồng thiết bị hiện hành là `mqtt-api.md` (mô hình device-native: thiết bị tự chạy auto; backend chỉ đọc telemetry, đọc/đặt ngưỡng, gửi lệnh thủ công dạng khoá phẳng pump/light/light_pwm/mode/config/message trên topic `xmini/control`). KHÔNG dùng `WATER_ON/LIGHT_ON/FAN_*`, không có rule-engine phía server.

# MCP Server — Plant Tree IoT Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Python MCP server that exposes 12 tools for Ollama to monitor and control Plant Tree IoT devices via the existing .NET REST API.

**Architecture:** MCP server (Python/FastMCP, stdio transport) wraps the .NET REST API running locally on Mac Mini at `http://localhost:5000`. Each tool group lives in its own module under `tools/` for easy extension. No auth in v1.0.

**Tech Stack:** Python 3.11+, `mcp` (FastMCP), `httpx`, `pytest`, `respx`

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `mcp-server/config.py` | API URL, timeout, server name |
| Create | `mcp-server/server.py` | FastMCP entry point, registers all tools |
| Create | `mcp-server/tools/__init__.py` | Empty package marker |
| Create | `mcp-server/tools/devices.py` | `list_devices`, `get_device_info` |
| Create | `mcp-server/tools/sensors.py` | `get_latest_sensor`, `get_sensor_history` |
| Create | `mcp-server/tools/control.py` | `send_command`, `get_pending_commands`, `auto_water`, `auto_light` |
| Create | `mcp-server/tools/rules.py` | `get_moisture_rule`, `set_moisture_rule`, `get_light_rule`, `set_light_rule` |
| Create | `mcp-server/requirements.txt` | Python dependencies |
| Create | `mcp-server/tests/__init__.py` | Empty package marker |
| Create | `mcp-server/tests/test_devices.py` | Tests for devices tools |
| Create | `mcp-server/tests/test_sensors.py` | Tests for sensors tools |
| Create | `mcp-server/tests/test_control.py` | Tests for control tools |
| Create | `mcp-server/tests/test_rules.py` | Tests for rules tools |

---

## REST API Endpoints Reference

| Tool | Method | Endpoint |
|---|---|---|
| `list_devices` | GET | `/api/devices` |
| `get_device_info` | GET | `/api/devices/{deviceId}` |
| `get_latest_sensor` | GET | `/api/sensordata/latest/{deviceId}` |
| `get_sensor_history` | GET | `/api/sensordata/history/{deviceId}?limit=N` |
| `send_command` | POST | `/api/control/commands` |
| `get_pending_commands` | GET | `/api/control/commands/{deviceId}` |
| `auto_water` | POST | `/api/control/auto-water/{deviceId}?threshold=30.0` |
| `auto_light` | POST | `/api/control/auto-light/{deviceId}?threshold=200.0` |
| `get_moisture_rule` | GET | `/api/rules/moisture/{deviceId}` |
| `set_moisture_rule` | POST | `/api/rules/moisture` |
| `get_light_rule` | GET | `/api/rules/light/{deviceId}` |
| `set_light_rule` | POST | `/api/rules/light` |

---

## Task 1: Project Setup

**Files:**
- Create: `mcp-server/requirements.txt`
- Create: `mcp-server/config.py`
- Create: `mcp-server/tools/__init__.py`
- Create: `mcp-server/tests/__init__.py`

- [ ] **Step 1: Tạo thư mục và requirements.txt**

```bash
mkdir -p mcp-server/tools mcp-server/tests
```

Nội dung `mcp-server/requirements.txt`:
```
mcp>=1.0.0
httpx>=0.27.0
pytest>=8.0.0
respx>=0.21.0
```

- [ ] **Step 2: Tạo config.py**

`mcp-server/config.py`:
```python
API_BASE_URL = "http://localhost:5000"
REQUEST_TIMEOUT = 10
MCP_SERVER_NAME = "plant-tree-mcp"
```

- [ ] **Step 3: Tạo __init__.py cho cả hai package**

`mcp-server/tools/__init__.py` — file rỗng

`mcp-server/tests/__init__.py` — file rỗng

- [ ] **Step 4: Cài dependencies**

```bash
cd mcp-server
python -m venv venv
source venv/bin/activate        # Mac/Linux
pip install -r requirements.txt
```

- [ ] **Step 5: Verify cài thành công**

```bash
python -c "import mcp; import httpx; import respx; print('OK')"
```

Expected output: `OK`

- [ ] **Step 6: Commit**

```bash
git add mcp-server/
git commit -m "feat: scaffold mcp-server project structure"
```

---

## Task 2: Devices Tools

**Files:**
- Create: `mcp-server/tools/devices.py`
- Create: `mcp-server/tests/test_devices.py`

- [ ] **Step 1: Viết failing test trước**

`mcp-server/tests/test_devices.py`:
```python
import respx
import httpx
from tools.devices import list_devices, get_device_info

BASE = "http://localhost:5000"

@respx.mock
def test_list_devices_returns_list():
    respx.get(f"{BASE}/api/devices").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "name": "Phòng khách", "isActive": True}
        ])
    )
    result = list_devices()
    assert isinstance(result, list)
    assert result[0]["deviceId"] == "dev1"

@respx.mock
def test_get_device_info_returns_device():
    respx.get(f"{BASE}/api/devices/dev1").mock(
        return_value=httpx.Response(200, json={
            "deviceId": "dev1", "name": "Phòng khách", "isActive": True
        })
    )
    result = get_device_info("dev1")
    assert result["deviceId"] == "dev1"
    assert result["isActive"] is True
```

- [ ] **Step 2: Chạy test để xác nhận FAIL**

```bash
cd mcp-server
pytest tests/test_devices.py -v
```

Expected: `ImportError: cannot import name 'list_devices' from 'tools.devices'`

- [ ] **Step 3: Implement devices.py**

`mcp-server/tools/devices.py`:
```python
import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

def list_devices() -> list:
    """Liệt kê tất cả thiết bị IoT đã đăng ký"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/devices")
        response.raise_for_status()
        return response.json()

def get_device_info(device_id: str) -> dict:
    """Lấy thông tin chi tiết một thiết bị: status, LastSeen, location, plantType"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/devices/{device_id}")
        response.raise_for_status()
        return response.json()
```

- [ ] **Step 4: Chạy test để xác nhận PASS**

```bash
pytest tests/test_devices.py -v
```

Expected:
```
tests/test_devices.py::test_list_devices_returns_list PASSED
tests/test_devices.py::test_get_device_info_returns_device PASSED
2 passed
```

- [ ] **Step 5: Commit**

```bash
git add mcp-server/tools/devices.py mcp-server/tests/test_devices.py
git commit -m "feat: add devices tools (list_devices, get_device_info)"
```

---

## Task 3: Sensors Tools

**Files:**
- Create: `mcp-server/tools/sensors.py`
- Create: `mcp-server/tests/test_sensors.py`

- [ ] **Step 1: Viết failing test**

`mcp-server/tests/test_sensors.py`:
```python
import respx
import httpx
from tools.sensors import get_latest_sensor, get_sensor_history

BASE = "http://localhost:5000"

@respx.mock
def test_get_latest_sensor_returns_data():
    respx.get(f"{BASE}/api/sensordata/latest/dev1").mock(
        return_value=httpx.Response(200, json={
            "deviceId": "dev1",
            "soilMoisture": 25.5,
            "temperature": 30.0,
            "humidity": 65.0,
            "lightLevel": 150.0
        })
    )
    result = get_latest_sensor("dev1")
    assert result["soilMoisture"] == 25.5
    assert result["temperature"] == 30.0

@respx.mock
def test_get_sensor_history_returns_list():
    respx.get(f"{BASE}/api/sensordata/history/dev1").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "soilMoisture": 25.5},
            {"deviceId": "dev1", "soilMoisture": 30.0}
        ])
    )
    result = get_sensor_history("dev1", limit=2)
    assert len(result) == 2
    assert result[0]["soilMoisture"] == 25.5
```

- [ ] **Step 2: Chạy test để xác nhận FAIL**

```bash
pytest tests/test_sensors.py -v
```

Expected: `ImportError: cannot import name 'get_latest_sensor'`

- [ ] **Step 3: Implement sensors.py**

`mcp-server/tools/sensors.py`:
```python
import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

def get_latest_sensor(device_id: str) -> dict:
    """Lấy dữ liệu cảm biến mới nhất: nhiệt độ, độ ẩm không khí, độ ẩm đất, ánh sáng, pH, mực nước"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/sensordata/latest/{device_id}")
        response.raise_for_status()
        return response.json()

def get_sensor_history(device_id: str, limit: int = 10) -> list:
    """Lấy N bản ghi cảm biến gần nhất của thiết bị"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(
            f"{API_BASE_URL}/api/sensordata/history/{device_id}",
            params={"limit": limit}
        )
        response.raise_for_status()
        return response.json()
```

- [ ] **Step 4: Chạy test để xác nhận PASS**

```bash
pytest tests/test_sensors.py -v
```

Expected:
```
tests/test_sensors.py::test_get_latest_sensor_returns_data PASSED
tests/test_sensors.py::test_get_sensor_history_returns_list PASSED
2 passed
```

- [ ] **Step 5: Commit**

```bash
git add mcp-server/tools/sensors.py mcp-server/tests/test_sensors.py
git commit -m "feat: add sensors tools (get_latest_sensor, get_sensor_history)"
```

---

## Task 4: Control Tools

**Files:**
- Create: `mcp-server/tools/control.py`
- Create: `mcp-server/tests/test_control.py`

- [ ] **Step 1: Viết failing test**

`mcp-server/tests/test_control.py`:
```python
import respx
import httpx
from tools.control import send_command, get_pending_commands, auto_water, auto_light

BASE = "http://localhost:5000"

@respx.mock
def test_send_valid_command():
    respx.post(f"{BASE}/api/control/commands").mock(
        return_value=httpx.Response(200, json={
            "message": "Command sent successfully",
            "commandId": "cmd1"
        })
    )
    result = send_command("dev1", "WATER_ON", duration=5000)
    assert result["commandId"] == "cmd1"

def test_send_invalid_command_returns_error():
    result = send_command("dev1", "INVALID_CMD")
    assert "error" in result

@respx.mock
def test_get_pending_commands():
    respx.get(f"{BASE}/api/control/commands/dev1").mock(
        return_value=httpx.Response(200, json=[
            {"id": "cmd1", "command": "WATER_ON", "executed": False}
        ])
    )
    result = get_pending_commands("dev1")
    assert result[0]["command"] == "WATER_ON"

@respx.mock
def test_auto_water_sends_command():
    respx.post(f"{BASE}/api/control/auto-water/dev1").mock(
        return_value=httpx.Response(200, json={
            "message": "Auto water command sent",
            "currentMoisture": 20.0,
            "threshold": 30.0
        })
    )
    result = auto_water("dev1", threshold=30.0)
    assert "currentMoisture" in result

@respx.mock
def test_auto_light_sends_command():
    respx.post(f"{BASE}/api/control/auto-light/dev1").mock(
        return_value=httpx.Response(200, json={
            "message": "Auto light command sent: LIGHT_ON",
            "currentLight": 100.0,
            "threshold": 200.0
        })
    )
    result = auto_light("dev1", threshold=200.0)
    assert "currentLight" in result
```

- [ ] **Step 2: Chạy test để xác nhận FAIL**

```bash
pytest tests/test_control.py -v
```

Expected: `ImportError: cannot import name 'send_command'`

- [ ] **Step 3: Implement control.py**

`mcp-server/tools/control.py`:
```python
import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

VALID_COMMANDS = ["WATER_ON", "WATER_OFF", "LIGHT_ON", "LIGHT_OFF", "FAN_ON", "FAN_OFF"]

def send_command(device_id: str, command: str, duration: int = 0) -> dict:
    """Gửi lệnh điều khiển đến thiết bị.
    command: WATER_ON, WATER_OFF, LIGHT_ON, LIGHT_OFF, FAN_ON, FAN_OFF
    duration: thời gian (ms), chỉ dùng cho WATER_ON
    """
    if command not in VALID_COMMANDS:
        return {"error": f"Lệnh không hợp lệ. Các lệnh hợp lệ: {', '.join(VALID_COMMANDS)}"}
    payload = {"deviceId": device_id, "command": command}
    if duration > 0:
        payload["parameters"] = {"duration": duration}
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(f"{API_BASE_URL}/api/control/commands", json=payload)
        response.raise_for_status()
        return response.json()

def get_pending_commands(device_id: str) -> list:
    """Xem danh sách lệnh đang chờ thiết bị thực thi"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/control/commands/{device_id}")
        response.raise_for_status()
        return response.json()

def auto_water(device_id: str, threshold: float = 30.0) -> dict:
    """Tưới tự động nếu độ ẩm đất < threshold (mặc định 30%)"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(
            f"{API_BASE_URL}/api/control/auto-water/{device_id}",
            params={"threshold": threshold}
        )
        response.raise_for_status()
        return response.json()

def auto_light(device_id: str, threshold: float = 200.0) -> dict:
    """Tự động bật đèn nếu ánh sáng < threshold, tắt nếu >= threshold (mặc định 200 lux)"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(
            f"{API_BASE_URL}/api/control/auto-light/{device_id}",
            params={"threshold": threshold}
        )
        response.raise_for_status()
        return response.json()
```

- [ ] **Step 4: Chạy test để xác nhận PASS**

```bash
pytest tests/test_control.py -v
```

Expected:
```
tests/test_control.py::test_send_valid_command PASSED
tests/test_control.py::test_send_invalid_command_returns_error PASSED
tests/test_control.py::test_get_pending_commands PASSED
tests/test_control.py::test_auto_water_sends_command PASSED
tests/test_control.py::test_auto_light_sends_command PASSED
5 passed
```

- [ ] **Step 5: Commit**

```bash
git add mcp-server/tools/control.py mcp-server/tests/test_control.py
git commit -m "feat: add control tools (send_command, get_pending_commands, auto_water, auto_light)"
```

---

## Task 5: Rules Tools

**Files:**
- Create: `mcp-server/tools/rules.py`
- Create: `mcp-server/tests/test_rules.py`

- [ ] **Step 1: Viết failing test**

`mcp-server/tests/test_rules.py`:
```python
import respx
import httpx
from tools.rules import get_moisture_rule, set_moisture_rule, get_light_rule, set_light_rule

BASE = "http://localhost:5000"

@respx.mock
def test_get_moisture_rule_returns_list():
    respx.get(f"{BASE}/api/rules/moisture/dev1").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "name": "Rule 1", "minMoisture": 30.0, "maxMoisture": 70.0}
        ])
    )
    result = get_moisture_rule("dev1")
    assert isinstance(result, list)
    assert result[0]["minMoisture"] == 30.0

@respx.mock
def test_set_moisture_rule_creates_rule():
    respx.post(f"{BASE}/api/rules/moisture").mock(
        return_value=httpx.Response(201, json={
            "deviceId": "dev1",
            "name": "Auto Water",
            "minMoisture": 25.0,
            "maxMoisture": 70.0,
            "waterDurationMs": 5000
        })
    )
    result = set_moisture_rule("dev1", "Auto Water", min_moisture=25.0, max_moisture=70.0)
    assert result["minMoisture"] == 25.0

@respx.mock
def test_get_light_rule_returns_list():
    respx.get(f"{BASE}/api/rules/light/dev1").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "name": "Light Rule", "minLight": 25.0, "maxLight": 60.0}
        ])
    )
    result = get_light_rule("dev1")
    assert isinstance(result, list)
    assert result[0]["minLight"] == 25.0

@respx.mock
def test_set_light_rule_creates_rule():
    respx.post(f"{BASE}/api/rules/light").mock(
        return_value=httpx.Response(201, json={
            "deviceId": "dev1",
            "name": "Auto Light",
            "minLight": 20.0,
            "maxLight": 60.0
        })
    )
    result = set_light_rule("dev1", "Auto Light", min_light=20.0, max_light=60.0)
    assert result["minLight"] == 20.0
```

- [ ] **Step 2: Chạy test để xác nhận FAIL**

```bash
pytest tests/test_rules.py -v
```

Expected: `ImportError: cannot import name 'get_moisture_rule'`

- [ ] **Step 3: Implement rules.py**

`mcp-server/tools/rules.py`:
```python
import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT

def get_moisture_rule(device_id: str) -> list:
    """Xem danh sách rule tưới nước tự động của thiết bị"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/rules/moisture/{device_id}")
        response.raise_for_status()
        return response.json()

def set_moisture_rule(
    device_id: str,
    name: str,
    min_moisture: float,
    max_moisture: float,
    water_duration_ms: int = 5000,
    cooldown_minutes: int = 30
) -> dict:
    """Tạo rule tưới tự động.
    Tưới khi độ ẩm đất < min_moisture, dừng khi > max_moisture.
    water_duration_ms: thời gian bơm (ms). cooldown_minutes: thời gian chờ giữa các lần tưới.
    """
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(f"{API_BASE_URL}/api/rules/moisture", json={
            "deviceId": device_id,
            "name": name,
            "minMoisture": min_moisture,
            "maxMoisture": max_moisture,
            "waterDurationMs": water_duration_ms,
            "isEnabled": True,
            "cooldownMinutes": cooldown_minutes
        })
        response.raise_for_status()
        return response.json()

def get_light_rule(device_id: str) -> list:
    """Xem danh sách rule đèn tự động của thiết bị"""
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.get(f"{API_BASE_URL}/api/rules/light/{device_id}")
        response.raise_for_status()
        return response.json()

def set_light_rule(
    device_id: str,
    name: str,
    min_light: float,
    max_light: float,
    cooldown_minutes: int = 10
) -> dict:
    """Tạo rule đèn tự động.
    Bật đèn khi ánh sáng < min_light, tắt khi > max_light.
    """
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        response = client.post(f"{API_BASE_URL}/api/rules/light", json={
            "deviceId": device_id,
            "name": name,
            "minLight": min_light,
            "maxLight": max_light,
            "isEnabled": True,
            "cooldownMinutes": cooldown_minutes
        })
        response.raise_for_status()
        return response.json()
```

- [ ] **Step 4: Chạy test để xác nhận PASS**

```bash
pytest tests/test_rules.py -v
```

Expected:
```
tests/test_rules.py::test_get_moisture_rule_returns_list PASSED
tests/test_rules.py::test_set_moisture_rule_creates_rule PASSED
tests/test_rules.py::test_get_light_rule_returns_list PASSED
tests/test_rules.py::test_set_light_rule_creates_rule PASSED
4 passed
```

- [ ] **Step 5: Commit**

```bash
git add mcp-server/tools/rules.py mcp-server/tests/test_rules.py
git commit -m "feat: add rules tools (moisture and light rules)"
```

---

## Task 6: Main Server Entry Point

**Files:**
- Create: `mcp-server/server.py`

- [ ] **Step 1: Chạy toàn bộ test suite để đảm bảo tất cả PASS trước khi build server**

```bash
pytest tests/ -v
```

Expected: `13 passed`

- [ ] **Step 2: Implement server.py**

`mcp-server/server.py`:
```python
from mcp.server.fastmcp import FastMCP
from config import MCP_SERVER_NAME
from tools.devices import list_devices, get_device_info
from tools.sensors import get_latest_sensor, get_sensor_history
from tools.control import send_command, get_pending_commands, auto_water, auto_light
from tools.rules import get_moisture_rule, set_moisture_rule, get_light_rule, set_light_rule

mcp = FastMCP(MCP_SERVER_NAME)

mcp.tool()(list_devices)
mcp.tool()(get_device_info)
mcp.tool()(get_latest_sensor)
mcp.tool()(get_sensor_history)
mcp.tool()(send_command)
mcp.tool()(get_pending_commands)
mcp.tool()(auto_water)
mcp.tool()(auto_light)
mcp.tool()(get_moisture_rule)
mcp.tool()(set_moisture_rule)
mcp.tool()(get_light_rule)
mcp.tool()(set_light_rule)

if __name__ == "__main__":
    mcp.run()
```

- [ ] **Step 3: Verify server import không lỗi**

```bash
python -c "import server; print('server.py OK')"
```

Expected: `server.py OK`

- [ ] **Step 4: Commit**

```bash
git add mcp-server/server.py
git commit -m "feat: add MCP server entry point with all 12 tools registered"
```

---

## Task 7: Kết nối Ollama trên Mac Mini

- [ ] **Step 1: Đảm bảo .NET API đang chạy**

```bash
# Trong thư mục plant-tree
cd PlantTreeIoTServer
dotnet run
# Verify: curl http://localhost:5000/api/devices
```

- [ ] **Step 2: Tìm file config của Ollama trên Mac**

```bash
# Mặc định Ollama lưu config tại
cat ~/.ollama/config.json
# Hoặc nếu dùng Open WebUI / Msty / AnythingLLM thì xem docs của tool đó
```

- [ ] **Step 3: Thêm MCP server vào Ollama config**

Thêm vào `~/.ollama/config.json` (hoặc tương đương):
```json
{
  "mcpServers": {
    "plant-tree": {
      "command": "python",
      "args": ["/absolute/path/to/mcp-server/server.py"]
    }
  }
}
```

Thay `/absolute/path/to/mcp-server/server.py` bằng đường dẫn thật trên Mac Mini.

- [ ] **Step 4: Restart Ollama và test**

```bash
# Restart Ollama
ollama stop && ollama serve

# Test conversation
ollama run <model-name>
>>> Liệt kê tất cả thiết bị IoT đang có
```

Expected: Ollama gọi tool `list_devices` và trả về danh sách thiết bị thật từ MongoDB.

- [ ] **Step 5: Final commit**

```bash
git add mcp-server/
git commit -m "feat: complete MCP server v1.0 — 12 tools for Plant Tree IoT"
```

---

## Upgrade Path (sau v1.0)

| Version | Việc cần làm |
|---|---|
| v1.1 | Thêm `tools/mqtt.py` — publish lệnh trực tiếp qua MQTT (192.168.88.126:1883) để nhanh hơn REST |
| v1.1 | Thêm API Key authentication vào header mọi request |
| v2.0 | Thêm `tools/analytics.py` — phân tích trend sensor, phát hiện bất thường, gợi ý rule tự động |
