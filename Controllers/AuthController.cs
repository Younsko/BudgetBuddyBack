namespace BudgetBuddy.Controllers;
using Microsoft.AspNetCore.Mvc;
using BudgetBuddy.Models;
using BudgetBuddy.Services;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;

    public AuthController(AppDbContext db, AuthService authService)
    {
        _db = db;
        _authService = authService;
    }

    /// <summary>
    /// Register a new user account
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        if (await _db.Users.AnyAsync(u => u.Username == dto.Username))
            return BadRequest("Username already taken");

        if (await _db.Users.AnyAsync(u => u.Email == dto.Email))
            return BadRequest("Email already registered");

        var user = new User
        {
            Name = dto.Name,
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = _authService.HashPassword(dto.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new AuthResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Token = _authService.GenerateToken(user)
        });
    }

    /// <summary>
    /// Login with username and password
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username);
        if (user == null || !_authService.VerifyPassword(dto.Password, user.PasswordHash))
            return Unauthorized("Invalid credentials");

        return Ok(new AuthResponseDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Token = _authService.GenerateToken(user)
        });
    }
}