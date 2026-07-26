using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PlantTreeIoTServer.Services;

// Cổng lấy từ biến môi trường PORT (mặc định 8000).
// docker-compose.deploy.yml map host 8080 -> container 8000.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8000";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://+:{port}");

// Add services to the container.
builder.Services.AddControllers();

// Register MongoDB service
builder.Services.AddSingleton<MongoDbService>();

// Register MQTT services
builder.Services.AddSingleton<MqttPublisherService>();
builder.Services.AddHostedService<PlantTreeIoTServer.Services.MqttBackgroundService>();

// Cờ chế độ auto/manual do server làm chủ (dùng chung controller + background service).
builder.Services.AddSingleton<DeviceModeStore>();

// ===== Authentication (JWT Bearer — người dùng / app / MCP service account) =====
// ESP32 dùng MQTT (HiveMQ) nên không cần xác thực HTTP riêng cho thiết bị.

// Bí mật ký JWT: Production BẮT BUỘC đặt biến môi trường JWT_SECRET (chuỗi ngẫu nhiên >= 32 ký tự).
// KHÔNG commit secret thật vào appsettings. Local (Development) dùng fallback trong code để chạy ngay.
var jwtSecret = Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"];

if (string.IsNullOrWhiteSpace(jwtSecret) && builder.Environment.IsDevelopment())
{
    jwtSecret = "dev-only-insecure-jwt-key-change-me-min-32-chars-0123456789";
}

if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException(
        "JWT secret chưa cấu hình hoặc < 32 ký tự. Đặt biến môi trường JWT_SECRET " +
        "bằng chuỗi ngẫu nhiên mạnh cho Production.");
}

builder.Services.AddSingleton(new JwtService(jwtSecret, builder.Configuration));

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
    });

// ===== AI server (tree-grow-helper) — proxy cho /api/assistant =====
// AI_SERVER_URL (env) ưu tiên, fallback AiServer:BaseUrl (appsettings), mặc định localhost:8787.
// Lời gọi .NET -> AI là NỘI BỘ, không gắn JWT (JWT chỉ áp cho lời gọi VÀO .NET API).
var aiServerUrl = Environment.GetEnvironmentVariable("AI_SERVER_URL")
    ?? builder.Configuration["AiServer:BaseUrl"]
    ?? "http://localhost:8787";

builder.Services.AddHttpClient<AiServerClient>(client =>
{
    client.BaseAddress = new Uri(aiServerUrl);
    client.Timeout = TimeSpan.FromSeconds(120); // LLM có thể chậm
});

// ===== Notify service (team thông báo) — .NET đẩy sự kiện cây sang qua webhook =====
// NOTIFY_URL + NOTIFY_API_KEY (env) hoặc Notify:BaseUrl / Notify:ApiKey (appsettings).
// Chưa cấu hình -> NotifyClient tự tắt (no-op), không chặn gì. Xem NOTIFY-INTEGRATION-GUIDE.md.
var notifyUrl = Environment.GetEnvironmentVariable("NOTIFY_URL")
    ?? builder.Configuration["Notify:BaseUrl"];

builder.Services.AddHttpClient(NotifyClient.HttpClientName, client =>
{
    if (!string.IsNullOrWhiteSpace(notifyUrl))
        client.BaseAddress = new Uri(notifyUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddSingleton<NotifyClient>();

// Configure CORS for ESP32 communication
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowESP32", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
// Khai báo bearer scheme để công cụ (Swagger UI/Scalar) hiện nút "Authorize" khi debug.
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, _) =>
    {
        document.Components ??= new Microsoft.OpenApi.OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, Microsoft.OpenApi.IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new Microsoft.OpenApi.OpenApiSecurityScheme
        {
            Type = Microsoft.OpenApi.SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = Microsoft.OpenApi.ParameterLocation.Header,
            Description = "Dán JWT: 'Bearer {token}'. Lấy token qua POST /api/auth/login hoặc /api/auth/dev-token.",
        };
        return Task.CompletedTask;
    });
});

var app = builder.Build();

// Initialize MQTT Publisher
var mqttPublisher = app.Services.GetRequiredService<MqttPublisherService>();
await mqttPublisher.InitializeAsync(app.Configuration);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// TLS được terminate ở edge (Cloudflare tunnel / reverse proxy) — tắt HTTPS redirect ngoài Development
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Use CORS
app.UseCors("AllowESP32");

// UseAuthentication PHẢI đứng trước UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
