# Plant Tree IoT Server

Server .NET để quản lý hệ thống IoT trồng cây thông minh với ESP32 và MongoDB.

## Tổng quan

Hệ thống bao gồm:

- **ESP32**: Thu thập dữ liệu cảm biến và thực hiện lệnh điều khiển
- **Server .NET**: API để trao đổi dữ liệu và quản lý thiết bị
- **MongoDB**: Lưu trữ dữ liệu cảm biến và thông tin thiết bị

## Cài đặt và Chạy

### Yêu cầu

- .NET 10.0
- MongoDB (local hoặc cloud)

### Chạy server

```bash
cd PlantTreeIoTServer
dotnet run
```

Server sẽ chạy trên `https://localhost:5001` và `http://localhost:5000`

## 🚀 Deployment

### Yêu cầu

- .NET 10.0 SDK
- Docker & Docker Compose (cho container deployment)
- MongoDB (local hoặc cloud)

### 1. Deploy với Docker (Khuyến nghị)

#### Chuẩn bị:

```bash
# Cập nhật MongoDB connection string trong appsettings.Production.json
# Đã được cấu hình sẵn để kết nối với MongoDB container
```

#### Chạy với Docker Compose:

```bash
# Từ thư mục root của project
docker-compose up -d

# Hoặc sử dụng script quản lý
./docker-manage.sh start
```

#### Các chế độ deployment:

```bash
# Development mode (ports 5000/5001)
./docker-manage.sh dev

# Production mode (ports 80/443 with Nginx)
./docker-manage.sh prod
```

Server sẽ chạy trên:

- **Development**: http://localhost:5000
- **Production**: http://localhost:80

#### Quản lý containers:

```bash
# Xem trạng thái
./docker-manage.sh status

# Xem logs
./docker-manage.sh logs

# Dừng containers
./docker-manage.sh stop

# Xây dựng lại
./docker-manage.sh build
```

📖 **Chi tiết**: Xem [DOCKER-README.md](DOCKER-README.md) để biết thêm thông tin về Docker deployment.

### 2. Deploy lên Azure (Cloud)

#### One-click Azure deployment:

```bash
# Setup hoàn chỉnh Azure environment
./setup-azure-complete.sh

# Hoặc từng bước:
./setup-azure-cosmos.sh     # Setup database
./deploy-azure.sh          # Deploy app
```

#### ESP32 code cho Azure:

```cpp
// Update SERVER_URL in esp32-azure-client.ino
const char* SERVER_URL = "http://your-azure-url";
```

📖 **Chi tiết**: Xem [AZURE-README.md](AZURE-README.md) để biết thêm thông tin về Azure deployment.

#### Chỉ build và chạy container:

```bash
# Build image
docker build -t planttree-iot-server ./PlantTreeIoTServer

# Chạy container
docker run -d -p 8080:80 -p 8443:443 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  --name planttree-server \
  planttree-iot-server
```

### 2. Deploy lên Windows Server với IIS

#### Cài đặt:

1. Cài đặt **IIS** và **ASP.NET Core Hosting Bundle**
2. Cài đặt **.NET Runtime** trên server
3. Cài đặt **MongoDB** hoặc sử dụng MongoDB Atlas

#### Publish ứng dụng:

```bash
cd PlantTreeIoTServer
dotnet publish -c Release -o ./publish
```

#### Cấu hình IIS:

1. Tạo Application Pool mới
2. Tạo Website trỏ đến thư mục `publish`
3. Cấu hình SSL certificate nếu cần

### 3. Deploy lên Linux Server

#### Với Nginx + .NET:

```bash
# Install .NET Runtime
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y aspnetcore-runtime-10.0

# Publish app
dotnet publish -c Release -o ./publish

# Run app
./publish/PlantTreeIoTServer --urls "http://0.0.0.0:5000"
```

#### Cấu hình Nginx reverse proxy:

```nginx
server {
    listen 80;
    server_name your-domain.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
    }
}
```

### 4. Deploy lên Cloud Platforms

#### Azure (Khuyến nghị):

##### Azure Container Instances (Dễ nhất):

```bash
# Deploy tự động
./deploy-azure.sh

# Hoặc setup từng bước
./setup-azure-cosmos.sh  # Setup database
./deploy-azure.sh        # Deploy app
```

##### Azure App Service:

```bash
# Deploy với App Service
./deploy-azure-appservice.sh
```

##### Azure Cosmos DB:

```bash
# Setup MongoDB-compatible database
./setup-azure-cosmos.sh
```

📖 **Chi tiết**: Xem [AZURE-README.md](AZURE-README.md) để biết thêm thông tin về Azure deployment.

#### AWS EC2:

```bash
# Install .NET on EC2
sudo yum install dotnet-sdk-10.0 -y

# Run app as service
sudo systemctl enable planttree-iot.service
sudo systemctl start planttree-iot.service
```

#### Heroku:

```bash
# Tạo Procfile
echo "web: ./PlantTreeIoTServer --urls http://+:$PORT" > Procfile

# Deploy
git push heroku main
```

## API Documentation

### 1. Quản lý thiết bị (Devices)

#### Đăng ký thiết bị mới

```
POST /api/devices/register
Content-Type: application/json

{
  "deviceId": "ESP32_001",
  "name": "Cây thông phòng khách",
  "location": "Phòng khách",
  "plantType": "Pine Tree"
}
```

#### Lấy tất cả thiết bị

```
GET /api/devices
```

#### Lấy thông tin thiết bị cụ thể

```
GET /api/devices/{deviceId}
```

#### Heartbeat từ ESP32

```
POST /api/devices/{deviceId}/heartbeat
```

### 2. Dữ liệu cảm biến (Sensor Data)

#### ESP32 gửi dữ liệu cảm biến

```
POST /api/sensordata/upload
Content-Type: application/json

{
  "deviceId": "ESP32_001",
  "temperature": 25.5,
  "humidity": 60.0,
  "soilMoisture": 45.0,
  "lightLevel": 300.0,
  "waterLevel": 85.0,
  "phLevel": 6.5,
  "location": "Phòng khách"
}
```

#### Lấy dữ liệu cảm biến mới nhất

```
GET /api/sensordata/latest/{deviceId}
```

#### Lấy lịch sử dữ liệu cảm biến

```
GET /api/sensordata/history/{deviceId}?limit=50
```

#### Lấy dữ liệu theo khoảng thời gian

```
GET /api/sensordata/range/{deviceId}?startDate=2024-01-01T00:00:00Z&endDate=2024-01-02T00:00:00Z
```

### 3. Điều khiển thiết bị (Control)

#### ESP32 lấy lệnh đang chờ

```
GET /api/control/commands/{deviceId}
```

#### Gửi lệnh điều khiển

```
POST /api/control/commands
Content-Type: application/json

{
  "deviceId": "ESP32_001",
  "command": "WATER_ON",
  "parameters": {
    "duration": 5000
  }
}
```

#### Báo cáo lệnh đã thực hiện

```
POST /api/control/commands/{commandId}/executed
```

#### Tự động tưới nước

```
POST /api/control/auto-water/{deviceId}?threshold=30.0
```

#### Tự động điều khiển đèn

```
POST /api/control/auto-light/{deviceId}?threshold=200.0
```

## Cấu hình MongoDB

Chỉnh sửa `appsettings.json`:

```json
{
  "MongoDbSettings": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "PlantTreeIoT"
  }
}
```

## ESP32 Integration

### Gửi dữ liệu cảm biến

```cpp
// ESP32 code example
#include <HTTPClient.h>
#include <ArduinoJson.h>

void sendSensorData() {
  HTTPClient http;
  http.begin("http://your-server:5000/api/sensordata/upload");
  http.addHeader("Content-Type", "application/json");

  DynamicJsonDocument doc(1024);
  doc["deviceId"] = "ESP32_001";
  doc["temperature"] = temperature;
  doc["humidity"] = humidity;
  doc["soilMoisture"] = soilMoisture;

  String jsonString;
  serializeJson(doc, jsonString);

  int httpResponseCode = http.POST(jsonString);
  http.end();
}
```

### Nhận lệnh điều khiển

```cpp
// ESP32 code example
void checkCommands() {
  HTTPClient http;
  http.begin("http://your-server:5000/api/control/commands/ESP32_001");

  int httpResponseCode = http.GET();
  if (httpResponseCode == 200) {
    String payload = http.getString();
    // Parse JSON và thực hiện lệnh
  }
  http.end();
}
```

## Database Schema

### SensorData Collection

```javascript
{
  "_id": ObjectId,
  "deviceId": "ESP32_001",
  "timestamp": ISODate,
  "temperature": 25.5,
  "humidity": 60.0,
  "soilMoisture": 45.0,
  "lightLevel": 300.0,
  "waterLevel": 85.0,
  "phLevel": 6.5,
  "location": "Phòng khách"
}
```

### Devices Collection

```javascript
{
  "_id": ObjectId,
  "deviceId": "ESP32_001",
  "name": "Cây thông phòng khách",
  "location": "Phòng khách",
  "plantType": "Pine Tree",
  "isActive": true,
  "lastSeen": ISODate,
  "createdAt": ISODate
}
```

### ControlCommands Collection

```javascript
{
  "_id": ObjectId,
  "deviceId": "ESP32_001",
  "command": "WATER_ON",
  "parameters": { "duration": 5000 },
  "executed": false,
  "executedAt": null,
  "createdAt": ISODate
}
```

## Lệnh điều khiển hỗ trợ

- `WATER_ON`: Bật máy bơm nước
- `WATER_OFF`: Tắt máy bơm nước
- `LIGHT_ON`: Bật đèn
- `LIGHT_OFF`: Tắt đèn
- `FAN_ON`: Bật quạt
- `FAN_OFF`: Tắt quạt

## Monitoring và Logging

Server ghi log các hoạt động quan trọng:

- Upload dữ liệu cảm biến
- Đăng ký thiết bị mới
- Gửi lệnh điều khiển
- Lỗi hệ thống

Xem log trong console hoặc cấu hình logging vào file.

## 🚀 Free Deployment Options

Nếu bạn quan tâm đến **chi phí**, chúng tôi cung cấp nhiều lựa chọn deployment **hoàn toàn miễn phí**:

### 1. Azure Free Tier (12 tháng miễn phí)

Azure cung cấp $200 credit trong 12 tháng đầu tiên cho tài khoản mới:

#### Tính năng miễn phí:

- **Azure Container Instances**: 2 containers miễn phí (1 vCPU, 1.5 GB RAM mỗi container)
- **Azure Cosmos DB**: 400 RU/s miễn phí, 5GB storage
- **Azure App Service**: 10 ứng dụng web, 1GB storage mỗi app

#### Setup Azure Free Tier:

```bash
# Setup hoàn chỉnh với free tier
./setup-azure-free.sh

# Hoặc từng bước
./setup-azure-cosmos-free.sh  # Setup Cosmos DB free tier
./deploy-azure-free.sh        # Deploy với free resources
```

📖 **Chi tiết**: Xem [FREE-OPTIONS.md](FREE-OPTIONS.md) để biết thêm thông tin về Azure free tier.

### 2. Railway (Hoàn toàn miễn phí)

Railway cung cấp hosting miễn phí với giới hạn hợp lý:

#### Tính năng miễn phí:

- **512MB RAM**, **0.5 vCPU**
- **512MB PostgreSQL** database
- **Unlimited bandwidth**
- **Custom domains** (với Cloudflare)

#### Deploy lên Railway:

```bash
# Cài đặt Railway CLI
npm install -g @railway/cli

# Login và deploy
railway login
railway init
railway up
```

#### Sử dụng script tự động:

```bash
# Deploy tự động lên Railway
./deploy-railway.sh
```

📖 **Chi tiết**: Xem [FREE-OPTIONS.md](FREE-OPTIONS.md) để biết thêm thông tin về Railway.

### 3. Local Development (Hoàn toàn miễn phí)

Chạy server trên máy tính cá nhân của bạn:

#### Yêu cầu:

- **Docker Desktop** (miễn phí)
- **.NET 10.0 SDK** (miễn phí)
- **MongoDB Community Server** (miễn phí)

#### Setup local development:

```bash
# Setup môi trường local
./setup-local.sh

# Chạy server
./run-local.sh
```

#### Truy cập:

- **Server**: http://localhost:5000
- **MongoDB**: localhost:27017
- **ESP32**: Cập nhật IP local trong code

📖 **Chi tiết**: Xem [FREE-OPTIONS.md](FREE-OPTIONS.md) để biết thêm thông tin về local development.

### 4. So sánh các lựa chọn Free

| Platform       | Chi phí       | Setup      | Performance       | Persistence |
| -------------- | ------------- | ---------- | ----------------- | ----------- |
| **Azure Free** | $0 (12 tháng) | Trung bình | Tốt               | Vĩnh viễn   |
| **Railway**    | $0            | Dễ         | Trung bình        | Vĩnh viễn   |
| **Local**      | $0            | Dễ         | Tốt (máy của bạn) | Máy local   |

### 5. Migration giữa các platform

Bạn có thể dễ dàng chuyển đổi giữa các platform:

```bash
# Từ local lên Railway
./migrate-to-railway.sh

# Từ Railway lên Azure
./migrate-to-azure.sh

# Backup/Restore data
./backup-data.sh
./restore-data.sh
```

### 6. ESP32 Configuration cho Free Platforms

#### Azure Free Tier:

```cpp
const char* SERVER_URL = "https://your-azure-free-url.azurewebsites.net";
```

#### Railway:

```cpp
const char* SERVER_URL = "https://your-project.up.railway.app";
```

#### Local Development:

```cpp
const char* SERVER_URL = "http://192.168.1.100:5000"; // IP của máy bạn
```

📖 **Chi tiết**: Xem [FREE-OPTIONS.md](FREE-OPTIONS.md) để biết hướng dẫn chi tiết.

## 🚀 Deploy từ Git (GitHub/GitLab)

Bạn có thể deploy server lên cloud platforms trực tiếp từ Git repository:

### 1. Railway (Khuyến nghị - Dễ nhất)

Railway tự động deploy khi bạn push code lên Git:

#### Setup Railway:

```bash
# Sử dụng script tự động
./deploy-github-railway.sh

# Hoặc setup thủ công:
# 1. Đăng ký tại https://railway.app
# 2. Connect GitHub repository
# 3. Railway tự động detect và deploy .NET app
```

#### Cấu hình Railway:

- **Runtime**: .NET 10.0
- **Build Command**: `dotnet publish -c Release -o ./publish`
- **Start Command**: `./publish/PlantTreeIoTServer`
- **Environment Variables**:
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `MONGODB_CONNECTION_STRING=[MongoDB Atlas URL]`

### 2. Render (Miễn phí 750 giờ/tháng)

Deploy Docker container từ Git:

#### Setup Render:

```bash
# Sử dụng script hướng dẫn
./deploy-github-render.sh

# Hoặc setup thủ công:
# 1. Đăng ký tại https://render.com
# 2. New Web Service -> Connect GitHub repo
# 3. Chọn Docker environment
```

#### Cấu hình Render:

- **Service**: Web Service
- **Environment**: Docker
- **Dockerfile Path**: `./Dockerfile`
- **Environment Variables**: Giống Railway

### 3. Vercel (Cho .NET apps)

Vercel hỗ trợ .NET runtime:

#### Setup Vercel:

```bash
# Sử dụng script tự động
./deploy-github-vercel.sh

# Hoặc setup thủ công:
# 1. Đăng ký tại https://vercel.com
# 2. Import Git repository
# 3. Vercel tự động detect .NET project
```

### 4. Azure với GitHub Actions (CI/CD)

Tự động deploy khi push code:

#### Setup GitHub Actions cho Azure:

```bash
# Tạo workflow CI/CD
./setup-github-azure.sh

# Cần setup secrets trong GitHub:
# AZURE_CREDENTIALS - Service Principal credentials
```

#### Workflow sẽ:

- Build .NET app khi push
- Run tests
- Deploy lên Azure App Service
- Auto-scale theo nhu cầu

### 5. ESP32 Code cho Git-deployed Apps

#### Railway:

```cpp
const char* SERVER_URL = "https://your-project.up.railway.app";
```

#### Render:

```cpp
const char* SERVER_URL = "https://your-service.onrender.com";
```

#### Vercel:

```cpp
const char* SERVER_URL = "https://your-project.vercel.app";
```

### 6. So sánh Git Deployment Platforms

| Platform    | Free Tier       | Setup      | Auto-deploy | Database        |
| ----------- | --------------- | ---------- | ----------- | --------------- |
| **Railway** | 512MB RAM       | Dễ nhất    | ✅          | PostgreSQL free |
| **Render**  | 750h/tháng      | Dễ         | ✅          | MongoDB Atlas   |
| **Vercel**  | 100GB bandwidth | Dễ         | ✅          | MongoDB Atlas   |
| **Azure**   | 12 tháng        | Trung bình | ✅          | Cosmos DB free  |

### 7. Lợi ích Deploy từ Git

- ✅ **Auto-deployment**: Push code = deploy ngay
- ✅ **Version control**: Mọi thay đổi được track
- ✅ **Rollback dễ dàng**: Quay lại version cũ
- ✅ **Collaboration**: Team có thể contribute
- ✅ **CI/CD**: Tự động test và build

### 8. Bắt đầu với Git Deployment

```bash
# 1. Commit code lên Git
git add .
git commit -m "Ready for deployment"
git push origin main

# 2. Chọn platform và setup
./deploy-github-railway.sh    # Railway (dễ nhất)
./deploy-github-render.sh     # Render (free tier tốt)
./deploy-github-vercel.sh     # Vercel (modern)

# 3. Update ESP32 với URL mới
# 4. Test API endpoints
```

📖 **Chi tiết**: Xem docs của từng platform để biết thêm thông tin.
