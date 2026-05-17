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
