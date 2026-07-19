> ℹ️ **Lưu ý lịch sử (2026-07-15):** Cơ chế auth (JWT Bearer) trong tài liệu này vẫn còn hiệu lực, nhưng các endpoint ĐIỀU KHIỂN được nhắc tới ở đây (`/api/control/auto-water`, `/api/control/auto-light`, `/api/rules/**`, lệnh `WATER_ON`, poll `control/commands`) đã bị GỠ. Hợp đồng thiết bị hiện hành là `mqtt-api.md` (device-native: lệnh khoá phẳng trên `xmini/control`, không có rule-engine phía server).

# Authentication đa người dùng — Plant Tree IoT Implementation Plan

> **For agentic workers:** Thực thi task-by-task. Mỗi task có bước verify riêng. Đây là thay đổi bảo mật + đa file, làm tuần tự và test kỹ từng task.

**Goal:** Thêm hệ thống đăng nhập đa người dùng (JWT) cho app/dashboard, xác thực riêng cho ESP32 (device token), và mô hình sở hữu thiết bị theo user (mỗi user chỉ thấy/điều khiển device của mình). Cập nhật MCP server và ESP32 để dùng cơ chế mới.

## Quyết định thiết kế (đã chốt)

| Vấn đề | Lựa chọn |
|---|---|
| User đăng nhập (app/dashboard) | **JWT Bearer token** (email + password) |
| ESP32 xác thực | **Token riêng mỗi thiết bị** — mỗi device 1 secret, gửi qua header |
| Quyền xem/điều khiển | **Mỗi user sở hữu device riêng** (Device có `OwnerId`) |
| Phạm vi đợt này | .NET API + MCP server (Python) + ESP32 + demo-dashboard (login) |

**Hai scheme xác thực song song:**
- `Bearer` (JWT) — dùng cho **con người** (app, dashboard, MCP server đóng vai service account).
- `DeviceKey` (custom handler) — dùng cho **ESP32** (upload sensor, poll lệnh, heartbeat, báo executed).

**Nguyên tắc phân loại endpoint:**
- Endpoint **ESP32 gọi** → scheme `DeviceKey`.
- Endpoint **người dùng gọi** → scheme `Bearer` (JWT) + kiểm tra `OwnerId`.
- Endpoint **đăng ký/đăng nhập** → không cần auth (anonymous).

**Tech Stack thêm mới:**
- `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.0)
- `BCrypt.Net-Next` (hash password và device secret)
- MCP server: dùng lại `httpx` (thêm login + Bearer header)

---

## File Map

| Action | Path | Trách nhiệm |
|---|---|---|
| Modify | `PlantTreeIoTServer/PlantTreeIoTServer.csproj` | Thêm 2 package auth |
| Create | `PlantTreeIoTServer/Models/AuthModels.cs` | `User`, DTO register/login/response |
| Create | `PlantTreeIoTServer/Services/JwtService.cs` | Sinh JWT token |
| Create | `PlantTreeIoTServer/Auth/DeviceKeyAuthenticationHandler.cs` | Xác thực ESP32 qua header |
| Create | `PlantTreeIoTServer/Controllers/AuthController.cs` | `register`, `login`, `me` |
| Modify | `PlantTreeIoTServer/Models/SensorModels.cs` | `Device` thêm `OwnerId`, `DeviceSecretHash` |
| Modify | `PlantTreeIoTServer/Services/MongoDbService.cs` | Collection `Users`, query theo owner, verify device secret |
| Modify | `PlantTreeIoTServer/Program.cs` | Cấu hình 2 scheme, `UseAuthentication` |
| Modify | `PlantTreeIoTServer/Controllers/DevicesController.cs` | `[Authorize]`, gán owner, sinh device secret, claim/rotate |
| Modify | `PlantTreeIoTServer/Controllers/SensorDataController.cs` | `upload` → DeviceKey; `latest/history/range` → JWT + owner |
| Modify | `PlantTreeIoTServer/Controllers/ControlController.cs` | `poll/executed` → DeviceKey; `send/auto-*` → JWT + owner |
| Modify | `PlantTreeIoTServer/Controllers/RulesController.cs` | Tất cả → JWT + owner |
| Modify | `PlantTreeIoTServer/appsettings.json` | Section `Jwt` |
| Modify | `mcp-server/config.py` | API key/login config |
| Create | `mcp-server/tools/api_client.py` | httpx client tự login + gắn Bearer, retry 401 |
| Modify | `mcp-server/tools/{devices,sensors,control,rules}.py` | Dùng `api_client` |
| Modify | `mcp-server/tests/*.py` | Mock login + Bearer |
| Modify | `esp32/*` + `README.md` + `API-GUIDE.md` | Header device token, hướng dẫn login |
| Modify | `demo-dashboard.html` | Login (JWT) + device secret; gắn header đúng loại endpoint |

---

## Endpoint Protection Matrix

| Endpoint | Ai gọi | Scheme | Ghi chú |
|---|---|---|---|
| `POST /api/auth/register` | app | — (anonymous) | Tạo user, trả JWT |
| `POST /api/auth/login` | app | — (anonymous) | Trả JWT |
| `GET /api/auth/me` | app | Bearer | Thông tin user hiện tại |
| `POST /api/devices/register` | app | Bearer | `OwnerId = user`, trả **device secret 1 lần** |
| `GET /api/devices` | app | Bearer | Chỉ device của user |
| `GET /api/devices/{id}` | app | Bearer | Phải là owner |
| `POST /api/devices/{id}/claim` | app | Bearer | Nhận sở hữu device chưa có owner |
| `POST /api/devices/{id}/rotate-secret` | app | Bearer | Sinh lại device secret |
| `POST /api/devices/{id}/heartbeat` | ESP32 | DeviceKey | |
| `POST /api/sensordata/upload` | ESP32 | DeviceKey | |
| `GET /api/sensordata/latest/{id}` | app | Bearer | Phải là owner |
| `GET /api/sensordata/history/{id}` | app | Bearer | Phải là owner |
| `GET /api/sensordata/range/{id}` | app | Bearer | Phải là owner |
| `GET /api/control/commands/{id}` (poll) | ESP32 | DeviceKey | |
| `POST /api/control/commands/{id}/executed` | ESP32 | DeviceKey | |
| `POST /api/control/commands` (send) | app | Bearer | Phải là owner |
| `POST /api/control/auto-water/{id}` | app | Bearer | Phải là owner |
| `POST /api/control/auto-light/{id}` | app | Bearer | Phải là owner |
| `GET/POST/PUT/DELETE /api/rules/**` | app | Bearer | Phải là owner của device |

> **Lưu ý MQTT:** ESP32 cũng có thể gửi sensor qua MQTT (`MqttBackgroundService`). Kênh MQTT xác thực bằng credential của broker (HiveMQ), **nằm ngoài phạm vi** task này. Chỉ HTTP API được bảo vệ ở đây. Ghi chú lại để xử lý sau nếu cần.

---

## Task 1: Cấu hình hạ tầng JWT + DeviceKey (chưa khoá endpoint)

Mục tiêu: cài package, thêm config, bật middleware auth. Endpoint vẫn mở (chưa gắn `[Authorize]`) để app không gãy giữa chừng.

**Files:** `PlantTreeIoTServer.csproj`, `appsettings.json`, `Program.cs`, `Services/JwtService.cs`, `Auth/DeviceKeyAuthenticationHandler.cs`

- [ ] **Step 1: Thêm package** vào `PlantTreeIoTServer.csproj`

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
```

```bash
cd PlantTreeIoTServer && dotnet restore
```

- [ ] **Step 2: Thêm section `Jwt` vào `appsettings.json`** — KHÔNG commit secret

```json
"Jwt": {
  "Issuer": "PlantTreeIoT",
  "Audience": "PlantTreeIoTUsers",
  "ExpiryMinutes": 1440
}
```

> ⚠️ **Không đặt `Secret` trong appsettings.json** (file này được commit git → lộ khóa ký = giả mạo token = bypass toàn bộ auth). Thay vào đó:
> - Production: **bắt buộc** biến môi trường `JWT_SECRET` (≥ 32 ký tự ngẫu nhiên). Thiếu → server không khởi động (fail-closed).
> - Local (Development): dùng fallback trong code (`Program.cs`), chỉ áp dụng khi `IsDevelopment()`.
> `ExpiryMinutes` 1440 = 24 giờ.

- [ ] **Step 3: Tạo `Services/JwtService.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PlantTreeIoTServer.Models;

namespace PlantTreeIoTServer.Services;

public class JwtService
{
    private readonly IConfiguration _config;
    public JwtService(IConfiguration config) => _config = config;

    private string Secret => Environment.GetEnvironmentVariable("JWT_SECRET")
        ?? _config["Jwt:Secret"]
        ?? throw new InvalidOperationException("JWT secret chưa được cấu hình");

    public string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id!),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var minutes = _config.GetValue<int?>("Jwt:ExpiryMinutes") ?? 10080;

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 4: Tạo `Auth/DeviceKeyAuthenticationHandler.cs`** (xác thực ESP32)

```csharp
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Auth;

public class DeviceKeyAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DeviceKey";
    private readonly MongoDbService _mongo;

    public DeviceKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        MongoDbService mongo) : base(options, logger, encoder)
    {
        _mongo = mongo;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Device-Id", out var deviceId) ||
            !Request.Headers.TryGetValue("X-Device-Secret", out var secret))
            return AuthenticateResult.Fail("Thiếu X-Device-Id hoặc X-Device-Secret");

        var device = await _mongo.GetDeviceAsync(deviceId!);
        if (device?.DeviceSecretHash == null ||
            !BCrypt.Net.BCrypt.Verify(secret!, device.DeviceSecretHash))
            return AuthenticateResult.Fail("Device secret không hợp lệ");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, device.DeviceId),
            new Claim("deviceId", device.DeviceId),
            new Claim(ClaimTypes.Role, "Device"),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
```

- [ ] **Step 5: Cấu hình auth trong `Program.cs`** (thêm trước `var app = builder.Build();`)

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PlantTreeIoTServer.Auth;

// ... sau builder.Services.AddControllers();

builder.Services.AddSingleton<JwtService>();

var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"]!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    })
    .AddScheme<AuthenticationSchemeOptions, DeviceKeyAuthenticationHandler>(
        DeviceKeyAuthenticationHandler.SchemeName, null);
```

Và bật middleware — thêm `app.UseAuthentication();` **NGAY TRƯỚC** `app.UseAuthorization();`:

```csharp
app.UseCors("AllowESP32");
app.UseAuthentication();   // <-- MỚI, phải đứng trước UseAuthorization
app.UseAuthorization();
app.MapControllers();
```

- [ ] **Step 6: Verify build**

```bash
cd PlantTreeIoTServer && dotnet build
```
Expected: build thành công, server chạy được như cũ (endpoint vẫn mở).

- [ ] **Step 7: Commit** — `feat: add JWT + DeviceKey auth infrastructure (endpoints still open)`

---

## Task 2: User model + AuthController (register / login / me)

**Files:** `Models/AuthModels.cs`, `Controllers/AuthController.cs`, sửa `MongoDbService.cs`

- [ ] **Step 1: Tạo `Models/AuthModels.cs`**

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PlantTreeIoTServer.Models;

[BsonIgnoreExtraElements]
public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [BsonElement("role")]
    public string Role { get; set; } = "User";   // "User" | "Admin"

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RegisterRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
```

- [ ] **Step 2: Thêm User operations vào `MongoDbService.cs`**

```csharp
public IMongoCollection<User> Users => _database.GetCollection<User>("Users");

public async Task<User?> GetUserByEmailAsync(string email)
    => await Users.Find(u => u.Email == email.ToLowerInvariant()).FirstOrDefaultAsync();

public async Task<User?> GetUserByIdAsync(string id)
    => await Users.Find(u => u.Id == id).FirstOrDefaultAsync();

public async Task CreateUserAsync(User user)
    => await Users.InsertOneAsync(user);
```

> Nên tạo unique index cho `email`. Có thể thêm trong constructor `MongoDbService`:
> ```csharp
> Users.Indexes.CreateOne(new CreateIndexModel<User>(
>     Builders<User>.IndexKeys.Ascending(u => u.Email),
>     new CreateIndexOptions { Unique = true }));
> ```

- [ ] **Step 3: Tạo `Controllers/AuthController.cs`**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly MongoDbService _mongo;
    private readonly JwtService _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(MongoDbService mongo, JwtService jwt, ILogger<AuthController> logger)
    {
        _mongo = mongo; _jwt = jwt; _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest("Email và password là bắt buộc");
        if (req.Password.Length < 6)
            return BadRequest("Password tối thiểu 6 ký tự");

        var email = req.Email.ToLowerInvariant();
        if (await _mongo.GetUserByEmailAsync(email) != null)
            return Conflict("Email đã được đăng ký");

        var user = new User
        {
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? email : req.DisplayName,
            Role = "User",
        };
        await _mongo.CreateUserAsync(user);

        return Ok(new AuthResponse
        {
            Token = _jwt.GenerateToken(user),
            Email = user.Email, DisplayName = user.DisplayName, Role = user.Role,
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var user = await _mongo.GetUserByEmailAsync(req.Email ?? "");
        if (user == null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Email hoặc password không đúng");

        return Ok(new AuthResponse
        {
            Token = _jwt.GenerateToken(user),
            Email = user.Email, DisplayName = user.DisplayName, Role = user.Role,
        });
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _mongo.GetUserByIdAsync(userId!);
        if (user == null) return NotFound();
        return Ok(new { user.Email, user.DisplayName, user.Role, user.CreatedAt });
    }
}
```

- [ ] **Step 4: Verify bằng curl**

```bash
# Đăng ký
curl -X POST http://localhost:5000/api/auth/register \
  -H "Content-Type: application/json" \
  -d '{"email":"test@a.com","password":"123456","displayName":"Test"}'
# -> trả về { "token": "eyJ...", ... }

# Đăng nhập
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"test@a.com","password":"123456"}'

# /me với token (thay $TOKEN)
curl http://localhost:5000/api/auth/me -H "Authorization: Bearer $TOKEN"
# Không có token -> 401
```

- [ ] **Step 5: Commit** — `feat: add User model and auth endpoints (register/login/me)`

---

## Task 3: Sở hữu thiết bị (ownership) + sinh device secret

**Files:** `Models/SensorModels.cs`, `Services/MongoDbService.cs`, `Controllers/DevicesController.cs`

- [ ] **Step 1: Thêm field vào `Device`** trong `SensorModels.cs`

```csharp
[BsonElement("ownerId")]
public string? OwnerId { get; set; }

[BsonElement("deviceSecretHash")]
public string? DeviceSecretHash { get; set; }
```

- [ ] **Step 2: Thêm helper vào `MongoDbService.cs`**

```csharp
public async Task<List<Device>> GetDevicesByOwnerAsync(string ownerId)
    => await Devices.Find(d => d.OwnerId == ownerId).ToListAsync();

// Trả về device nếu user là owner, ngược lại null (dùng để chặn truy cập chéo)
public async Task<Device?> GetOwnedDeviceAsync(string deviceId, string ownerId)
    => await Devices.Find(d => d.DeviceId == deviceId && d.OwnerId == ownerId)
                    .FirstOrDefaultAsync();

public async Task SetDeviceSecretAsync(string deviceId, string secretHash)
{
    var update = Builders<Device>.Update.Set(d => d.DeviceSecretHash, secretHash);
    await Devices.UpdateOneAsync(d => d.DeviceId == deviceId, update);
}

public async Task SetDeviceOwnerAsync(string deviceId, string ownerId)
{
    var update = Builders<Device>.Update.Set(d => d.OwnerId, ownerId);
    await Devices.UpdateOneAsync(d => d.DeviceId == deviceId, update);
}
```

- [ ] **Step 3: Sửa `DevicesController.cs`** — thêm `[Authorize]` cấp class, gán owner, sinh secret

Điểm chính:
- Thêm `[Authorize]` trên class (mặc định scheme Bearer). `heartbeat` sẽ chuyển sang DeviceKey ở Task 4.
- Helper lấy userId: `User.FindFirstValue(ClaimTypes.NameIdentifier)`.
- `RegisterDevice`: set `OwnerId = userId`, sinh secret ngẫu nhiên, lưu **hash**, trả **plaintext 1 lần**.
- `GetAllDevices` → chỉ device của user.
- `GetDevice` → dùng `GetOwnedDeviceAsync`.
- Thêm `claim` (nhận device chưa có owner — cho device cũ) và `rotate-secret`.

```csharp
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
// ...

[ApiController]
[Route("api/[controller]")]
[Authorize]   // mặc định = Bearer (JWT)
public class DevicesController : ControllerBase
{
    // ... ctor giữ nguyên

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private static string GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [HttpPost("register")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceRegistrationRequest request)
    {
        if (string.IsNullOrEmpty(request.DeviceId) || string.IsNullOrEmpty(request.Name))
            return BadRequest("DeviceId and Name are required");

        if (await _mongoDbService.GetDeviceAsync(request.DeviceId) != null)
            return Conflict($"Device {request.DeviceId} already exists");

        var secret = GenerateSecret();
        var device = new Device
        {
            DeviceId = request.DeviceId,
            Name = request.Name,
            Location = request.Location,
            PlantType = request.PlantType,
            OwnerId = UserId,
            DeviceSecretHash = BCrypt.Net.BCrypt.HashPassword(secret),
            IsActive = true,
            LastSeen = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        };
        await _mongoDbService.CreateDeviceAsync(device);

        // Trả secret PLAINTEXT đúng 1 lần — nạp vào firmware ESP32, server không lưu lại
        return CreatedAtAction(nameof(GetDevice), new { deviceId = device.DeviceId },
            new { device.DeviceId, device.Name, deviceSecret = secret });
    }

    [HttpGet]
    public async Task<IActionResult> GetAllDevices()
        => Ok(await _mongoDbService.GetDevicesByOwnerAsync(UserId));

    [HttpGet("{deviceId}")]
    public async Task<IActionResult> GetDevice(string deviceId)
    {
        var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
        if (device == null) return NotFound($"Device {deviceId} not found");
        return Ok(device);
    }

    // Nhận sở hữu device cũ (chưa có owner) — dùng khi migrate dữ liệu cũ
    [HttpPost("{deviceId}/claim")]
    public async Task<IActionResult> Claim(string deviceId)
    {
        var device = await _mongoDbService.GetDeviceAsync(deviceId);
        if (device == null) return NotFound();
        if (!string.IsNullOrEmpty(device.OwnerId))
            return Conflict("Device đã có chủ sở hữu");

        await _mongoDbService.SetDeviceOwnerAsync(deviceId, UserId);
        var secret = GenerateSecret();
        await _mongoDbService.SetDeviceSecretAsync(deviceId,
            BCrypt.Net.BCrypt.HashPassword(secret));
        return Ok(new { deviceId, deviceSecret = secret });
    }

    // Sinh lại secret (nếu lộ hoặc quên)
    [HttpPost("{deviceId}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(string deviceId)
    {
        var device = await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId);
        if (device == null) return NotFound();
        var secret = GenerateSecret();
        await _mongoDbService.SetDeviceSecretAsync(deviceId,
            BCrypt.Net.BCrypt.HashPassword(secret));
        return Ok(new { deviceId, deviceSecret = secret });
    }

    // heartbeat: chuyển sang DeviceKey ở Task 4
}
```

- [ ] **Step 4: Verify** — login lấy token, đăng ký device, kiểm tra response có `deviceSecret`; user khác không thấy device này.

```bash
curl -X POST http://localhost:5000/api/devices/register \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"deviceId":"esp32-001","name":"Cay phong khach"}'
# -> { "deviceId":"esp32-001", "deviceSecret":"...LƯU LẠI..." }

curl http://localhost:5000/api/devices -H "Authorization: Bearer $TOKEN"   # chỉ thấy device của mình
curl http://localhost:5000/api/devices                                     # 401
```

- [ ] **Step 5: Commit** — `feat: device ownership + per-device secret on register/claim/rotate`

---

## Task 4: Gắn scheme DeviceKey cho các endpoint ESP32

Các endpoint ESP32 gọi: `sensordata/upload`, `control/commands/{deviceId}` (poll), `control/commands/{commandId}/executed`, `devices/{deviceId}/heartbeat`.

- [ ] **Step 1: Đánh dấu từng action** bằng:

```csharp
[Authorize(AuthenticationSchemes = DeviceKeyAuthenticationHandler.SchemeName)]
```

(nhớ `using PlantTreeIoTServer.Auth;`)

Áp cho:
- `SensorDataController.UploadSensorData`
- `ControlController.GetPendingCommands`
- `ControlController.MarkCommandExecuted`
- `DevicesController.DeviceHeartbeat` (đặt riêng attribute này, đè `[Authorize]` mặc định của class)

- [ ] **Step 2: Chống giả mạo deviceId** — trong `UploadSensorData`, ép `deviceId` từ token, không tin body:

```csharp
var authDeviceId = User.FindFirstValue("deviceId");
if (!string.IsNullOrEmpty(authDeviceId))
    request.DeviceId = authDeviceId;   // device chỉ được upload cho chính nó
```

Tương tự, ở `GetPendingCommands(deviceId)` kiểm tra `deviceId == User.FindFirstValue("deviceId")` → nếu khác trả `403`.

- [ ] **Step 3: Verify** — gọi upload không header → 401; có `X-Device-Id` + `X-Device-Secret` đúng → 200.

```bash
curl -X POST http://localhost:5000/api/sensordata/upload \
  -H "X-Device-Id: esp32-001" -H "X-Device-Secret: <secret>" \
  -H "Content-Type: application/json" \
  -d '{"deviceId":"esp32-001","soilMoisture":20,"lightLevel":15}'
```

- [ ] **Step 4: Commit** — `feat: protect ESP32 endpoints with DeviceKey scheme`

---

## Task 5: Bảo vệ + owner-scope SensorData / Control / Rules (endpoint người dùng)

- [ ] **Step 1: SensorDataController** — thêm `[Authorize]` cấp class (Bearer). `upload` đã có DeviceKey (đè lại). Với `latest/history/range`: kiểm tra ownership:

```csharp
private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

// đầu mỗi action latest/history/range:
if (await _mongoDbService.GetOwnedDeviceAsync(deviceId, UserId) == null)
    return NotFound($"Device {deviceId} not found");
```

- [ ] **Step 2: ControlController** — `[Authorize]` cấp class. `poll` + `executed` đã gắn DeviceKey. Với `SendCommand`, `AutoWater`, `AutoLight`: kiểm tra `GetOwnedDeviceAsync(deviceId, UserId)` trước khi thao tác (SendCommand lấy deviceId từ body).

- [ ] **Step 3: RulesController** — `[Authorize]` cấp class. Mọi action nhận `deviceId` (create) hoặc `ruleId` (update/delete):
  - Create: kiểm tra owner của `request.DeviceId`.
  - Update/Delete theo `ruleId`: load rule → lấy `deviceId` của rule → kiểm tra owner. (Thêm `GetLightRuleAsync(ruleId)` nếu chưa có, tương tự `GetMoistureRuleAsync`.)

- [ ] **Step 4: Verify toàn bộ** — user A không truy cập được device của user B (403/404); ESP32 vẫn upload/poll được; các thao tác của chính owner hoạt động bình thường.

- [ ] **Step 5: Commit** — `feat: authorize + owner-scope sensor/control/rules endpoints`

---

## Task 6: Cập nhật MCP server (Python) — login + Bearer

MCP server đóng vai **service account** (một user riêng). Đăng nhập lấy JWT, gắn `Authorization: Bearer` vào mọi request, tự login lại khi 401.

**Files:** `mcp-server/config.py`, `mcp-server/tools/api_client.py` (mới), `tools/*.py`, `tests/*.py`

- [ ] **Step 1: Sửa `config.py`**

```python
import os

API_BASE_URL = os.getenv("PLANT_API_URL", "http://localhost:5000")
REQUEST_TIMEOUT = 10
MCP_SERVER_NAME = "plant-tree-mcp"

# Tài khoản service account của MCP (tạo trước bằng /api/auth/register)
MCP_USER_EMAIL = os.getenv("PLANT_MCP_EMAIL", "mcp@plant-tree.local")
MCP_USER_PASSWORD = os.getenv("PLANT_MCP_PASSWORD", "change-me")
```

- [ ] **Step 2: Tạo `tools/api_client.py`** — httpx client tự đính token, login lại khi hết hạn

```python
import httpx
from config import API_BASE_URL, REQUEST_TIMEOUT, MCP_USER_EMAIL, MCP_USER_PASSWORD

_token: str | None = None

def _login() -> str:
    resp = httpx.post(
        f"{API_BASE_URL}/api/auth/login",
        json={"email": MCP_USER_EMAIL, "password": MCP_USER_PASSWORD},
        timeout=REQUEST_TIMEOUT,
    )
    resp.raise_for_status()
    return resp.json()["token"]

def _headers() -> dict:
    global _token
    if _token is None:
        _token = _login()
    return {"Authorization": f"Bearer {_token}"}

def request(method: str, path: str, **kwargs):
    """Gọi API kèm Bearer. Nếu 401 -> login lại 1 lần rồi thử lại."""
    global _token
    url = f"{API_BASE_URL}{path}"
    with httpx.Client(timeout=REQUEST_TIMEOUT) as client:
        resp = client.request(method, url, headers=_headers(), **kwargs)
        if resp.status_code == 401:
            _token = None
            resp = client.request(method, url, headers=_headers(), **kwargs)
        resp.raise_for_status()
        return resp.json()
```

- [ ] **Step 3: Refactor 12 tool** dùng `api_client.request(...)`. Ví dụ `tools/devices.py`:

```python
from tools.api_client import request

def list_devices() -> list:
    """Liệt kê tất cả thiết bị IoT của tài khoản"""
    return request("GET", "/api/devices")

def get_device_info(device_id: str) -> dict:
    """Lấy thông tin chi tiết một thiết bị"""
    return request("GET", f"/api/devices/{device_id}")
```

Áp tương tự cho `sensors.py`, `control.py`, `rules.py` (POST dùng `json=payload`, query dùng `params=...`).

- [ ] **Step 4: Cập nhật tests** — mock endpoint `/api/auth/login` trả token, và assert header `Authorization` được gửi. Ví dụ thêm vào mỗi test file:

```python
@respx.mock
def test_list_devices_returns_list():
    respx.post(f"{BASE}/api/auth/login").mock(
        return_value=httpx.Response(200, json={"token": "faketoken"}))
    respx.get(f"{BASE}/api/devices").mock(
        return_value=httpx.Response(200, json=[{"deviceId": "dev1"}]))
    result = list_devices()
    assert result[0]["deviceId"] == "dev1"
```

> Lưu ý: `_token` là biến module-level → reset giữa các test (fixture `autouse` set `api_client._token = None`).

- [ ] **Step 5: Chạy test** — `cd mcp-server && pytest -v` → tất cả PASS.

- [ ] **Step 6: Tạo service account** thật trên server + đặt env var, rồi test end-to-end với Ollama.

- [ ] **Step 7: Commit** — `feat: mcp-server authenticates via JWT service account`

---

## Task 7: Cập nhật ESP32 + tài liệu

- [ ] **Step 1: ESP32** — thêm 2 header vào mọi request HTTP:

```cpp
const char* DEVICE_ID = "esp32-001";
const char* DEVICE_SECRET = "...secret nhan tu /api/devices/register...";

http.addHeader("X-Device-Id", DEVICE_ID);
http.addHeader("X-Device-Secret", DEVICE_SECRET);
```

Áp cho: gửi sensor (`/api/sensordata/upload`), poll lệnh (`/api/control/commands/{id}`), báo executed, heartbeat.

- [ ] **Step 2: README.md + API-GUIDE.md** — thêm mục Authentication:
  - Flow: user register/login → nhận JWT → gọi API kèm `Authorization: Bearer`.
  - Device: đăng ký device (JWT) → nhận `deviceSecret` (1 lần) → nạp firmware → ESP32 gửi `X-Device-Id`/`X-Device-Secret`.
  - Cập nhật mọi ví dụ `curl` thêm header token.

- [ ] **Step 3: Commit** — `docs: update ESP32 + API guide for auth (device token & JWT)`

> demo-dashboard được xử lý riêng ở **Task 8** (thêm login + device secret vào Live Tester).

---

## Task 8: demo-dashboard — thêm login (JWT) + device secret

Dashboard (`demo-dashboard.html`) là một file HTML tĩnh có phần **Live Tester** gọi API thật qua 2 hàm `run()` và `apiJson()` (đều dùng `fetch(cfg().base + path)`). Nó test **cả hai loại endpoint**, nên phải gắn header theo đúng loại:

| Nút test | Endpoint | Loại auth |
|---|---|---|
| `run-ping`, `run-d-reg`, `run-d-list`, `run-d-get` | devices | **user** (Bearer) |
| `run-s-latest`, `run-s-history` | sensordata (đọc) | **user** (Bearer) |
| `run-rm-*`, `run-rl-*`, rules manager (PUT/DELETE) | rules | **user** (Bearer) |
| `run-c-send`, `run-c-aw`, `run-c-al` | control (người dùng) | **user** (Bearer) |
| `run-d-hb` | heartbeat | **device** (X-Device-*) |
| `run-s-upload` | sensordata/upload | **device** (X-Device-*) |
| `run-c-pending` | control/commands/{id} (poll) | **device** (X-Device-*) |

**Files:** `demo-dashboard.html`

- [ ] **Step 1: Thêm UI login + device secret** vào section `try-config`, ngay dưới `.conn-bar` (khoảng dòng 1243)

```html
<div class="conn-bar">
  <div class="field">
    <label for="conn-email">Email</label>
    <input id="conn-email" style="width:200px" placeholder="test@a.com">
  </div>
  <div class="field">
    <label for="conn-password">Password</label>
    <input id="conn-password" type="password" style="width:150px" placeholder="••••••">
  </div>
  <button class="run-btn" id="btn-login">Đăng nhập</button>
  <button class="run-btn" id="btn-register" style="background:#334155;color:#e2e8f0">Đăng ký</button>
  <div class="conn-status">Auth: <b id="auth-status">chưa đăng nhập</b></div>
</div>

<div class="conn-bar">
  <div class="field" style="flex:1">
    <label for="conn-device-secret">Device Secret (giả lập ESP32 — dùng cho upload/heartbeat/poll)</label>
    <input id="conn-device-secret" style="width:100%" placeholder="dán deviceSecret nhận khi đăng ký device">
  </div>
</div>
```

- [ ] **Step 2: Mở rộng state + `cfg()`** trong IIFE Live Tester (khoảng dòng 2334–2352)

```js
const LS = 'planttree-tester-cfg';
const hostEl = document.getElementById('conn-host');
const portEl = document.getElementById('conn-port');
const devEl  = document.getElementById('conn-device');
const emailEl = document.getElementById('conn-email');
const pwEl = document.getElementById('conn-password');
const secretEl = document.getElementById('conn-device-secret');
const authStatusEl = document.getElementById('auth-status');
if (!hostEl) return;

const saved = (() => { try { return JSON.parse(localStorage.getItem(LS)) || {}; } catch { return {}; } })();
hostEl.value = saved.host || 'localhost';
portEl.value = saved.port || '8000';
devEl.value  = saved.device || 'ESP32S3_Zone1';
if (secretEl) secretEl.value = saved.deviceSecret || '';
let authToken = saved.token || '';

function cfg() {
  return {
    base: `http://${(hostEl.value.trim() || 'localhost')}:${(portEl.value.trim() || '8000')}`,
    device: devEl.value.trim(),
    deviceSecret: secretEl ? secretEl.value.trim() : '',
    token: authToken,
  };
}
function persist() {
  localStorage.setItem(LS, JSON.stringify({
    host: hostEl.value.trim(), port: portEl.value.trim(), device: devEl.value.trim(),
    deviceSecret: secretEl ? secretEl.value.trim() : '', token: authToken,
  }));
  refreshEcho();
}
[hostEl, portEl, devEl, secretEl].forEach(el => el && el.addEventListener('input', persist));

function setAuthStatus() {
  authStatusEl.textContent = authToken ? '✓ đã đăng nhập' : 'chưa đăng nhập';
  authStatusEl.style.color = authToken ? '#4ade80' : '#94a3b8';
}
setAuthStatus();
```

- [ ] **Step 3: Header theo loại auth** — thêm helper và sửa `run()` + `apiJson()`

```js
function authHeaders(auth) {
  const h = { 'Content-Type': 'application/json' };
  if (auth === 'device') {
    h['X-Device-Id'] = cfg().device;
    h['X-Device-Secret'] = cfg().deviceSecret;
  } else if (authToken) {
    h['Authorization'] = 'Bearer ' + authToken;
  }
  return h;
}
```

Trong `run(resultId, method, path, body, auth = 'user')` đổi dòng tạo `opts`:
```js
const opts = { method, headers: authHeaders(auth) };
```
Tương tự `apiJson(method, path, body, auth = 'user')`:
```js
const opts = { method, headers: authHeaders(auth) };
```

- [ ] **Step 4: Login / Register** — thêm handler

```js
async function doAuth(path) {
  const email = emailEl.value.trim(), password = pwEl.value;
  if (!email || !password) { authStatusEl.textContent = '✗ nhập email + password'; return; }
  try {
    const res = await fetch(cfg().base + path, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password, displayName: email }),
    });
    const data = await res.json();
    if (res.ok && data.token) { authToken = data.token; persist(); setAuthStatus(); }
    else { authStatusEl.textContent = '✗ ' + (data.message || data || res.status); authStatusEl.style.color = '#fca5a5'; }
  } catch (e) { authStatusEl.textContent = '✗ ' + e.message; authStatusEl.style.color = '#fca5a5'; }
}
on('btn-login', () => doAuth('/api/auth/login'));
on('btn-register', () => doAuth('/api/auth/register'));
```

- [ ] **Step 5: Đánh dấu 3 nút device-type** — thêm tham số `'device'`

```js
on('run-d-hb', () => { const d = requireDevice('r-d-hb'); if (d) run('r-d-hb', 'POST', `/api/devices/${encodeURIComponent(d)}/heartbeat`, undefined, 'device'); });
on('run-s-upload', () => { /* ...build body... */ run('r-s-upload', 'POST', '/api/sensordata/upload', body, 'device'); });
on('run-c-pending', () => { const d = requireDevice('r-c-pending'); if (d) run('r-c-pending', 'GET', `/api/control/commands/${encodeURIComponent(d)}`, undefined, 'device'); });
```

Các nút còn lại giữ nguyên (mặc định `'user'`).

- [ ] **Step 6: Hiển thị device secret sau khi đăng ký device** — sửa handler `run-d-reg` để bắt `deviceSecret` từ response và tự điền vào ô secret

```js
on('run-d-reg', async () => {
  const id = val('d-reg-id') || cfg().device;
  await run('r-d-reg', 'POST', '/api/devices/register', {
    deviceId: id, name: val('d-reg-name'), plantType: val('d-reg-plant'), location: val('d-reg-loc')
  });
  // sau khi register, đọc secret từ ô kết quả để tự lưu (device secret chỉ trả 1 lần)
  try {
    const txt = document.querySelector('#r-d-reg pre').textContent;
    const secret = JSON.parse(txt).deviceSecret;
    if (secret && secretEl) { secretEl.value = secret; persist(); }
  } catch {}
});
```

> Ghi chú UI: cập nhật `.alert.info` ở tab Kết nối — nhắc user **đăng nhập trước**, và device secret lấy từ response đăng ký device (chỉ hiện 1 lần).

- [ ] **Step 7: Verify trên trình duyệt**
  1. Mở dashboard → tab Kết nối → Đăng ký/Đăng nhập → status `✓ đã đăng nhập`.
  2. Ping server (`GET /api/devices`) → 200 (trước khi login → 401).
  3. Đăng ký device → response có `deviceSecret`, ô secret tự điền.
  4. Upload sensor (device-type) với secret đúng → 200; xoá secret → 401.
  5. List devices → chỉ thấy device của user đang đăng nhập.

- [ ] **Step 8: Commit** — `feat: demo-dashboard login (JWT) + device secret for ESP32 endpoints`

---

## Task 9: Verify end-to-end + migrate dữ liệu cũ

- [ ] **Step 1: Migrate device cũ** — device tạo trước khi có auth không có `OwnerId`/secret. Với mỗi device cũ: user đăng nhập → `POST /api/devices/{id}/claim` để nhận sở hữu + lấy secret mới → nạp lại ESP32.

- [ ] **Step 2: Kịch bản kiểm thử đầy đủ:**
  1. Register user A, user B.
  2. A đăng ký `esp32-001` → lấy secret.
  3. A xem được `esp32-001`; B **không** xem được (404).
  4. ESP32 (secret của 001) upload sensor OK; sai secret → 401.
  5. A gửi lệnh WATER_ON cho `esp32-001` OK; B gửi → 403/404.
  6. ESP32 poll lệnh với DeviceKey OK.
  7. MCP server (service account) list/control device của nó OK.
  8. Không token → mọi endpoint người dùng trả 401.
  9. demo-dashboard: đăng nhập → ping OK; upload sensor bằng device secret OK; toàn bộ flow chạy end-to-end trên trình duyệt.

- [ ] **Step 3: Siết CORS (khuyến nghị)** — trong `Program.cs` đổi `AllowAnyOrigin()` thành `WithOrigins("<domain dashboard>")` để chặn website lạ gọi API bằng token đánh cắp qua trình duyệt.

- [ ] **Step 4: Commit cuối** — `test: verify multi-user auth end-to-end`

---

## Lưu ý bảo mật quan trọng

1. **JWT_SECRET**: đặt bằng biến môi trường trên Railway, tối thiểu 32 ký tự ngẫu nhiên. **Đừng commit secret vào appsettings.json** — server đã fail-closed (không khởi động ở Production nếu thiếu/ngắn). Local dùng fallback trong `Program.cs` chỉ khi `IsDevelopment()`.
2. **Device secret**: chỉ trả plaintext **đúng 1 lần** lúc register/claim/rotate; server chỉ lưu BCrypt hash.
3. **HTTPS**: Railway/Render đã TLS ở edge → token không đi qua mạng dạng plaintext. Với LAN nội bộ (`http://`) cần cân nhắc.
4. **MQTT** vẫn là kênh riêng — xác thực bằng credential broker, chưa gắn với user. Xử lý sau nếu muốn phân quyền cả MQTT.
5. **Dashboard** sau khi khoá endpoint sẽ cần đăng nhập — đã xử lý ở Task 8 (login lấy JWT + ô device secret để giả lập ESP32).

## Tài liệu tham khảo

- JWT Bearer trong ASP.NET Core: https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication
- Custom Authentication Handler: https://learn.microsoft.com/aspnet/core/security/authentication/
- Policy/Authorize theo scheme: https://learn.microsoft.com/aspnet/core/security/authorization/limitingidentitybyscheme
- BCrypt.Net-Next: https://github.com/BcryptNet/bcrypt.net
- CORS: https://learn.microsoft.com/aspnet/core/security/cors
- OWASP API Security Top 10: https://owasp.org/API-Security/
- httpx (MCP client): https://www.python-httpx.org/quickstart/#custom-headers
