using PlantTreeIoTServer.Services;

// Support Railway's dynamic PORT environment variable
var port = Environment.GetEnvironmentVariable("PORT") ?? "80";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://+:{port}");

// Add services to the container.
builder.Services.AddControllers();

// Register MongoDB service
builder.Services.AddSingleton<MongoDbService>();

// Register MQTT background service
builder.Services.AddHostedService<PlantTreeIoTServer.Services.MqttBackgroundService>();

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

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Railway/Render handle SSL at the edge - disable HTTPS redirect in production
if (app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Use CORS
app.UseCors("AllowESP32");

app.UseAuthorization();

app.MapControllers();

app.Run();
