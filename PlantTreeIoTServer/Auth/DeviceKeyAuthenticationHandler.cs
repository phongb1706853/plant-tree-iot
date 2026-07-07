using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Auth;

/// <summary>
/// Xác thực cho thiết bị ESP32 (không đăng nhập như người dùng).
/// Thiết bị gửi 2 header: X-Device-Id + X-Device-Secret.
/// Server so khớp secret với hash lưu trong DB (BCrypt).
/// </summary>
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
            !Request.Headers.TryGetValue("X-Device-Secret", out var secret) ||
            string.IsNullOrWhiteSpace(deviceId) ||
            string.IsNullOrWhiteSpace(secret))
        {
            return AuthenticateResult.Fail("Thiếu X-Device-Id hoặc X-Device-Secret");
        }

        var device = await _mongo.GetDeviceAsync(deviceId!);
        if (device?.DeviceSecretHash == null ||
            !BCrypt.Net.BCrypt.Verify(secret!, device.DeviceSecretHash))
        {
            return AuthenticateResult.Fail("Device secret không hợp lệ");
        }

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
