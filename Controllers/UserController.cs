namespace BudgetBuddy.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BudgetBuddy.Models;
using BudgetBuddy.Services;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly ExchangeRateService _exchangeRate;
    private readonly ILogger<UserController> _logger;

    public UserController(
        AppDbContext db, 
        AuthService authService, 
        ExchangeRateService exchangeRate,
        ILogger<UserController> logger)
    {
        _db = db;
        _authService = authService;
        _exchangeRate = exchangeRate;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

    /// <summary>
    /// Get current user profile with statistics
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<UserProfileDto>> GetProfile()
    {
        var userId = GetUserId();
        var user = await _db.Users
            .Include(u => u.Categories)
            .Include(u => u.Transactions)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound(new { error = "User not found" });

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Name = user.Name,
            Username = user.Username,
            Email = user.Email,
            PreferredCurrency = user.PreferredCurrency,
            ProfilePhotoUrl = user.ProfilePhotoUrl,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            TotalCategories = user.Categories.Count,
            TotalTransactions = user.Transactions.Count
        });
    }

    /// <summary>
    /// Update user profile (name, email, password, currency, photo)
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(UserProfileDto), 200)]
    [ProducesResponseType(400)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile(UserUpdateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
            return NotFound(new { error = "User not found" });

        try
        {
            // Update name
            if (!string.IsNullOrWhiteSpace(dto.Name))
            {
                user.Name = dto.Name.Trim();
                _logger.LogInformation($"User {userId} updated name");
            }

            // Update email (check uniqueness)
            if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
            {
                if (await _db.Users.AnyAsync(u => u.Email == dto.Email && u.Id != userId))
                    return BadRequest(new { error = "Email already in use" });

                user.Email = dto.Email.Trim();
                _logger.LogInformation($"User {userId} updated email");
            }

            // Update password
            if (!string.IsNullOrWhiteSpace(dto.Password))
            {
                user.PasswordHash = _authService.HashPassword(dto.Password);
                _logger.LogInformation($"User {userId} changed password");
            }

            // Update preferred currency
            if (!string.IsNullOrWhiteSpace(dto.PreferredCurrency))
            {
                user.PreferredCurrency = dto.PreferredCurrency.ToUpper();
                _logger.LogInformation($"User {userId} updated currency to {user.PreferredCurrency}");
            }

            // Update profile photo URL
            if (dto.ProfilePhotoUrl != null)
            {
                // Validate URL format
                if (!string.IsNullOrEmpty(dto.ProfilePhotoUrl) && 
                    !Uri.TryCreate(dto.ProfilePhotoUrl, UriKind.Absolute, out _))
                {
                    return BadRequest(new { error = "Invalid profile photo URL" });
                }

                user.ProfilePhotoUrl = dto.ProfilePhotoUrl;
                _logger.LogInformation($"User {userId} updated profile photo");
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new UserProfileDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                PreferredCurrency = user.PreferredCurrency,
                ProfilePhotoUrl = user.ProfilePhotoUrl,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt,
                TotalCategories = await _db.Categories.CountAsync(c => c.UserId == userId),
                TotalTransactions = await _db.Transactions.CountAsync(t => t.UserId == userId)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Profile update error for user {userId}: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while updating profile" });
        }
    }

    /// <summary>
    /// Get comprehensive monthly statistics
    /// </summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(StatsDto), 200)]
    public async Task<ActionResult<StatsDto>> GetStats([FromQuery] int year = 0, [FromQuery] int month = 0)
    {
        var userId = GetUserId();
        var now = DateTime.UtcNow;
        if (year == 0) year = now.Year;
        if (month == 0) month = now.Month;

        try
        {
            var user = await _db.Users
                .Include(u => u.Categories)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound(new { error = "User not found" });

            var transactions = await _db.Transactions
                .Where(t => t.UserId == userId
                    && t.TransactionDate.Year == year
                    && t.TransactionDate.Month == month)
                .Include(t => t.Category)
                .ToListAsync();

            var stats = new StatsDto
            {
                TotalTransactions = transactions.Count
            };

            // By category
            foreach (var cat in user.Categories)
            {
                var categoryTransactions = transactions.Where(t => t.CategoryId == cat.Id).ToList();
                var spent = categoryTransactions.Sum(t => t.Amount);

                stats.ByCategory.Add(new CategorySpendingDto
                {
                    CategoryName = cat.Name,
                    Color = cat.Color,
                    Spent = spent,
                    Budget = cat.MonthlyBudget,
                    TransactionCount = categoryTransactions.Count
                });
                stats.TotalBudgetThisMonth += cat.MonthlyBudget;
            }

            // By currency with conversion
            var byCurrency = transactions.GroupBy(t => t.Currency);
            foreach (var group in byCurrency)
            {
                var totalInCurrency = group.Sum(t => t.Amount);
                var converted = await _exchangeRate.ConvertCurrency(
                    totalInCurrency,
                    group.Key,
                    user.PreferredCurrency
                );

                stats.ByCurrency.Add(new CurrencySpendingDto
                {
                    Currency = group.Key,
                    Amount = totalInCurrency,
                    ConvertedToPreferred = converted
                });
                stats.TotalSpentThisMonth += converted;
            }

            // Daily spending for charts
            var dailyGroups = transactions
                .GroupBy(t => t.TransactionDate.Date)
                .OrderBy(g => g.Key);

            foreach (var group in dailyGroups)
            {
                stats.DailySpending.Add(new DailySpendingDto
                {
                    Date = group.Key,
                    Amount = group.Sum(t => t.Amount),
                    TransactionCount = group.Count()
                });
            }

            _logger.LogInformation($"Stats generated for user {userId}: {year}/{month}");

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Stats error for user {userId}: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while fetching statistics" });
        }
    }

    /// <summary>
    /// Delete user account (with all associated data)
    /// </summary>
    [HttpDelete("profile")]
    [ProducesResponseType(200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteAccount([FromBody] string password)
    {
        var userId = GetUserId();
        var user = await _db.Users.FindAsync(userId);

        if (user == null)
            return NotFound(new { error = "User not found" });

        // Verify password before deletion
        if (!_authService.VerifyPassword(password, user.PasswordHash))
            return Unauthorized(new { error = "Invalid password" });

        try
        {
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            _logger.LogWarning($"User account deleted: {user.Username} (ID: {userId})");

            return Ok(new { message = "Account deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Account deletion error for user {userId}: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while deleting account" });
        }
    }
}