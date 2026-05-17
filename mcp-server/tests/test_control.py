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
