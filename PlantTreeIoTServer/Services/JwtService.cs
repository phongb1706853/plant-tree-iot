using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using PlantTreeIoTServer.Models;

namespace PlantTreeIoTServer.Services;

/// <summary>
/// Sinh JWT token cho người dùng (app / dashboard / MCP service account).
/// </summary>
public class JwtService
{
    private readonly IConfiguration _config;
    private readonly string _secret;

    // Secret được resolve + validate một lần ở Program.cs rồi inject vào đây
    // (một nguồn sự thật duy nhất cho cả ký và xác thực token).
    public JwtService(string secret, IConfiguration config)
    {
        _secret = secret;
        _config = config;
    }

    public string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id!),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var minutes = _config.GetValue<int?>("Jwt:ExpiryMinutes") ?? 1440; // mặc định 24 giờ

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
