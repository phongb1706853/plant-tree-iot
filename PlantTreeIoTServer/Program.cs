using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PlantTreeIoTServer.Auth;
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

// ===== Authentication =====
// - JWT Bearer: cho người dùng (app / dashboard / MCP service account)
// - DeviceKey:  cho ESP32 (header X-Device-Id + X-Device-Secret)

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
    })
    .AddScheme<AuthenticationSchemeOptions, DeviceKeyAuthenticationHandler>(
        DeviceKeyAuthenticationHandler.SchemeName, null);

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
builder.Services.AddOpenApi();

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
