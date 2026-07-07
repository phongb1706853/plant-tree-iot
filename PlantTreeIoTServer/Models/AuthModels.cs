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
    [System.Text.Json.Serialization.JsonIgnore] // không bao giờ trả hash ra API response
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
