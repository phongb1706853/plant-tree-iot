using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PlantTreeIoTServer.Models;
using PlantTreeIoTServer.Services;

namespace PlantTreeIoTServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly MongoDbService _mongo;
    private readonly JwtService _jwt;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AuthController> _logger;

    public AuthController(MongoDbService mongo, JwtService jwt, IWebHostEnvironment env, ILogger<AuthController> logger)
    {
        _mongo = mongo;
        _jwt = jwt;
        _env = env;
        _logger = logger;
    }

    // Hash cố định để so khớp khi email không tồn tại -> thời gian phản hồi không lộ email có/không tồn tại
    private static readonly string DummyPasswordHash =
        BCrypt.Net.BCrypt.HashPassword("dummy-password-for-constant-time-login");

    /// <summary>Đăng ký người dùng mới, trả về JWT.</summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest req)
    {
        try
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
            _logger.LogInformation("User registered: {Email}", email);

            return Ok(new AuthResponse
            {
                Token = _jwt.GenerateToken(user),
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering user");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Đăng nhập, trả về JWT.</summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        try
        {
            var user = await _mongo.GetUserByEmailAsync(req.Email ?? "");
            // Luôn chạy BCrypt.Verify (kể cả khi user==null, dùng dummy hash) để tránh lộ email qua timing
            var hashToCheck = user?.PasswordHash ?? DummyPasswordHash;
            var passwordOk = BCrypt.Net.BCrypt.Verify(req.Password ?? "", hashToCheck);
            if (user == null || !passwordOk)
                return Unauthorized("Email hoặc password không đúng");

            return Ok(new AuthResponse
            {
                Token = _jwt.GenerateToken(user),
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging in");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>Thông tin user đang đăng nhập.</summary>
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var user = await _mongo.GetUserByIdAsync(userId);
        if (user == null) return NotFound();

        return Ok(new { user.Email, user.DisplayName, user.Role, user.CreatedAt });
    }

    /// <summary>
    /// [CHỈ Development] Lấy nhanh JWT bearer để debug (curl/Swagger) mà không cần đăng ký.
    /// Tự seed (hoặc dùng lại) user cố định dev@plant-tree.local. Production trả 404 (ẩn hoàn toàn).
    /// </summary>
    [HttpPost("dev-token")]
    public async Task<IActionResult> DevToken()
    {
        if (!_env.IsDevelopment())
            return NotFound();

        try
        {
            const string devEmail = "dev@plant-tree.local";
            var user = await _mongo.GetUserByEmailAsync(devEmail);
            if (user == null)
            {
                user = new User
                {
                    Email = devEmail,
                    // Hash cố định — chỉ dùng nội bộ Development, không dành cho login thật.
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("dev-only-do-not-use-in-prod"),
                    DisplayName = "Dev Debug User",
                    Role = "User",
                };
                await _mongo.CreateUserAsync(user);
                _logger.LogInformation("Dev user seeded: {Email}", devEmail);
            }

            return Ok(new AuthResponse
            {
                Token = _jwt.GenerateToken(user),
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error issuing dev token");
            return StatusCode(500, "Internal server error");
        }
    }
}
