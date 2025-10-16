namespace BudgetBuddy.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BudgetBuddy.Models;
using BudgetBuddy.Services;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/user")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuthService _authService;
    private readonly ExchangeRateService _exchangeRate;

    public UserController(AppDbContext db, AuthService authService, ExchangeRateService exchangeRate)
    {
        _db = db;
        _authService = authService;
        _exchangeRate = exchangeRate;
    }

    private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

    /// <summary>
    /// Get current user profile
    /// </summary>
    [HttpGet("profile")]
    public async Task<ActionResult<User>> GetProfile()
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null) return NotFound();

        return Ok(new { user.Id, user.Name, user.Username, user.Email, user.PreferredCurrency });
    }

    /// <summary>
    /// Update user profile (name, email, password, currency)
    /// </summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(UserUpdateDto dto)
    {
        var user = await _db.Users.FindAsync(GetUserId());
        if (user == null) return NotFound();

        if (!string.IsNullOrEmpty(dto.Name)) user.Name = dto.Name;
        if (!string.IsNullOrEmpty(dto.Email)) user.Email = dto.Email;
        if (!string.IsNullOrEmpty(dto.Password)) user.PasswordHash = _authService.HashPassword(dto.Password);
        if (!string.IsNullOrEmpty(dto.PreferredCurrency)) user.PreferredCurrency = dto.PreferredCurrency.ToUpper();

        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok("Profile updated");
    }

    /// <summary>
    /// Get monthly statistics (total spent, by category, by currency)
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<StatsDto>> GetStats([FromQuery] int year = 0, [FromQuery] int month = 0)
    {
        var now = DateTime.UtcNow;
        if (year == 0) year = now.Year;
        if (month == 0) month = now.Month;

        var user = await _db.Users.Include(u => u.Categories).FirstOrDefaultAsync(u => u.Id == GetUserId());
        if (user == null) return NotFound();

        var transactions = await _db.Transactions
            .Where(t => t.UserId == GetUserId()
                && t.TransactionDate.Year == year
                && t.TransactionDate.Month == month)
            .Include(t => t.Category)
            .ToListAsync();

        var stats = new StatsDto();

        // By category
        foreach (var cat in user.Categories)
        {
            var spent = transactions.Where(t => t.CategoryId == cat.Id).Sum(t => t.Amount);
            stats.ByCategory.Add(new CategorySpendingDto
            {
                CategoryName = cat.Name,
                Spent = spent,
                Budget = cat.MonthlyBudget
            });
            stats.TotalBudgetThisMonth += cat.MonthlyBudget;
        }

        // By currency
        var byCurrency = transactions.GroupBy(t => t.Currency);
        foreach (var group in byCurrency)
        {
            var converted = await _exchangeRate.ConvertCurrency(
                group.Sum(t => t.Amount),
                group.Key,
                user.PreferredCurrency
            );
            stats.ByCurrency.Add(new CurrencySpendingDto
            {
                Currency = group.Key,
                Amount = group.Sum(t => t.Amount),
                ConvertedToPreferred = converted
            });
            stats.TotalSpentThisMonth += converted;
        }

        return Ok(stats);
    }
}
