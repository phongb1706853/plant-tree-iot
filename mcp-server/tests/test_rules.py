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
