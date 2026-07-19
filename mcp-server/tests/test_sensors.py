import respx
import httpx
from tools.sensors import get_latest_sensor, get_sensor_history

BASE = "http://localhost:5000"

@respx.mock
def test_get_latest_sensor_returns_data():
    respx.get(f"{BASE}/api/sensordata/latest/dev1").mock(
        return_value=httpx.Response(200, json={
            "deviceId": "dev1",
            "soilPercent": 25,
            "temperature": 30.0,
            "humidity": 65.0,
            "lightLevel": 150.0,
            "batteryPercent": 75,
            "mode": "auto"
        })
    )
    result = get_latest_sensor("dev1")
    assert result["soilPercent"] == 25
    assert result["temperature"] == 30.0

@respx.mock
def test_get_sensor_history_returns_list():
    respx.get(f"{BASE}/api/sensordata/history/dev1").mock(
        return_value=httpx.Response(200, json=[
            {"deviceId": "dev1", "soilPercent": 25},
            {"deviceId": "dev1", "soilPercent": 30}
        ])
    )
    result = get_sensor_history("dev1", limit=2)
    assert len(result) == 2
    assert result[0]["soilPercent"] == 25
