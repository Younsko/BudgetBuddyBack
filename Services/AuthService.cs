namespace BudgetBuddy.Services;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using BudgetBuddy.Models;

public class AuthService
{
    private readonly IConfiguration _config;

    public AuthService(IConfiguration config) => _config = config;

    public string HashPassword(string password)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_config["JWT_SECRET"] ?? "secret")))
        {
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hash);
        }
    }

    public bool VerifyPassword(string password, string hash)
    {
        var hashOfInput = HashPassword(password);
        return hashOfInput.Equals(hash);
    }

    public string GenerateToken(User user)
    {
        var jwtSecret = _config["JWT_SECRET"] ?? "super-secret-key-change-this-in-production-super-secret-key-change";
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _config["JWT_ISSUER"] ?? "BudgetBuddy",
            audience: _config["JWT_AUDIENCE"] ?? "BudgetBuddy",
            claims: new[] {
                new System.Security.Claims.Claim("id", user.Id.ToString()),
                new System.Security.Claims.Claim("username", user.Username),
                new System.Security.Claims.Claim("email", user.Email)
            },
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}