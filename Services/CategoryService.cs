namespace BudgetBuddy.Services;
using BudgetBuddy.Models;
using Microsoft.EntityFrameworkCore;

public class CategoryService
{
    private readonly AppDbContext _db;
    private readonly ILogger<CategoryService> _logger;

    public CategoryService(AppDbContext db, ILogger<CategoryService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Catégories par défaut créées à l'inscription
    /// </summary>
    private static readonly List<(string Name, string Color, decimal Budget)> DefaultCategories = new()
    {
        ("Food", "#FF6B6B", 300m),
        ("Transport", "#4ECDC4", 150m),
        ("Healthcare", "#45B7D1", 100m),
        ("Entertainment", "#FFA07A", 100m),
        ("Education", "#98D8C8", 200m),
        ("Housing", "#6C5CE7", 500m),
        ("Utilities", "#FDCB6E", 150m),
        ("Shopping", "#E17055", 200m),
        ("Miscellaneous", "#A29BFE", 100m)
    };

    /// <summary>
    /// Initialize default categories for new user
    /// </summary>
    public async Task InitializeDefaultCategoriesAsync(int userId)
    {
        _logger.LogInformation($"Initializing default categories for user {userId}");

        var categories = DefaultCategories.Select(dc => new Category
        {
            UserId = userId,
            Name = dc.Name,
            Color = dc.Color,
            MonthlyBudget = dc.Budget,
            CreatedAt = DateTime.UtcNow
        }).ToList();

        _db.Categories.AddRange(categories);
        await _db.SaveChangesAsync();

        _logger.LogInformation($"Created {categories.Count} default categories for user {userId}");
    }

    /// <summary>
    /// Get all categories with spending info for user
    /// </summary>
    public async Task<List<CategoryDto>> GetCategoriesWithSpendingAsync(int userId, int? year = null, int? month = null)
    {
        var now = DateTime.UtcNow;
        var targetYear = year ?? now.Year;
        var targetMonth = month ?? now.Month;

        var categories = await _db.Categories
            .Where(c => c.UserId == userId)
            .ToListAsync();

        var result = new List<CategoryDto>();

        foreach (var cat in categories)
        {
            var spent = await _db.Transactions
                .Where(t => t.CategoryId == cat.Id
                    && t.TransactionDate.Year == targetYear
                    && t.TransactionDate.Month == targetMonth)
                .SumAsync(t => t.Amount);

            var transactionCount = await _db.Transactions
                .Where(t => t.CategoryId == cat.Id
                    && t.TransactionDate.Year == targetYear
                    && t.TransactionDate.Month == targetMonth)
                .CountAsync();

            result.Add(new CategoryDto
            {
                Id = cat.Id,
                Name = cat.Name,
                Color = cat.Color,
                MonthlyBudget = cat.MonthlyBudget,
                SpentThisMonth = spent,
                TransactionCount = transactionCount
            });
        }

        return result.OrderBy(c => c.Name).ToList();
    }

    /// <summary>
    /// Check if user owns category
    /// </summary>
    public async Task<bool> UserOwnsCategoryAsync(int userId, int categoryId)
    {
        return await _db.Categories.AnyAsync(c => c.Id == categoryId && c.UserId == userId);
    }
}