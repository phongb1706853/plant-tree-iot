#!/bin/bash

# Plant Tree IoT API Test Script
# Usage: ./test-api.sh [base_url]

BASE_URL=${1:-"http://localhost:8080"}

echo "🧪 Testing Plant Tree IoT API at $BASE_URL"
echo "=========================================="

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Test function
test_endpoint() {
    local method=$1
    local endpoint=$2
    local expected_status=${3:-200}
    local description=$4

    echo -n "Testing $description... "

    if [ "$method" = "GET" ]; then
        response=$(curl -s -o /dev/null -w "%{http_code}" "$BASE_URL$endpoint")
    elif [ "$method" = "POST" ]; then
        response=$(curl -s -o /dev/null -w "%{http_code}" -X POST \
                  -H "Content-Type: application/json" \
                  -d "$5" "$BASE_URL$endpoint")
    fi

    if [ "$response" -eq "$expected_status" ]; then
        echo -e "${GREEN}✓ PASS${NC} ($response)"
    else
        echo -e "${RED}✗ FAIL${NC} (expected $expected_status, got $response)"
    fi
}

# Wait for service to be ready
echo "⏳ Waiting for service to be ready..."
for i in {1..30}; do
    if curl -s "$BASE_URL/api/devices" > /dev/null 2>&1; then
        break
    fi
    sleep 2
done

# Test health check
test_endpoint "GET" "/" 404 "Root endpoint (should return 404)"

# Test devices endpoints
test_endpoint "GET" "/api/devices" 200 "Get all devices"
test_endpoint "GET" "/api/devices/ESP32_TEST" 404 "Get non-existent device"

# Test device registration
test_endpoint "POST" "/api/devices/register" 201 "Register new device" '{
  "deviceId": "ESP32_TEST",
  "name": "Test Device",
  "location": "Test Location",
  "plantType": "Test Plant"
}'

# Test sensor data upload
test_endpoint "POST" "/api/sensordata/upload" 200 "Upload sensor data" '{
  "deviceId": "ESP32_TEST",
  "temperature": 25.5,
  "humidity": 60.0,
  "soilMoisture": 45.0
}'

# Test get latest sensor data
test_endpoint "GET" "/api/sensordata/latest/ESP32_TEST" 200 "Get latest sensor data"

# Test get sensor history
test_endpoint "GET" "/api/sensordata/history/ESP32_TEST?limit=10" 200 "Get sensor data history"

# Test control commands
test_endpoint "GET" "/api/control/commands/ESP32_TEST" 200 "Get pending commands"

# Test auto water
test_endpoint "POST" "/api/control/auto-water/ESP32_TEST" 200 "Auto water command"

echo ""
echo "=========================================="
echo "🎉 API testing completed!"
echo ""
echo "📊 To view detailed responses, run individual curl commands:"
echo "curl $BASE_URL/api/devices"
echo "curl $BASE_URL/api/sensordata/latest/ESP32_TEST"