namespace BudgetBuddy.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BudgetBuddy.Models;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public CategoriesController(AppDbContext db) => _db = db;

    private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

    /// <summary>
    /// List all categories for current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> GetCategories()
    {
        var now = DateTime.UtcNow;
        var categories = await _db.Categories
            .Where(c => c.UserId == GetUserId())
            .ToListAsync();

        var result = new List<CategoryDto>();
        foreach (var cat in categories)
        {
            var spent = await _db.Transactions
                .Where(t => t.CategoryId == cat.Id
                    && t.TransactionDate.Year == now.Year
                    && t.TransactionDate.Month == now.Month)
                .SumAsync(t => t.Amount);

            result.Add(new CategoryDto
            {
                Id = cat.Id,
                Name = cat.Name,
                Color = cat.Color,
                MonthlyBudget = cat.MonthlyBudget,
                SpentThisMonth = spent
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Create a new category
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CategoryDto>> CreateCategory(CategoryCreateDto dto)
    {
        var category = new Category
        {
            UserId = GetUserId(),
            Name = dto.Name,
            Color = dto.Color,
            MonthlyBudget = dto.MonthlyBudget
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCategories), new
        {
            Id = category.Id,
            Name = category.Name,
            Color = category.Color,
            MonthlyBudget = category.MonthlyBudget,
            SpentThisMonth = 0
        });
    }

    /// <summary>
    /// Update a category
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(int id, CategoryCreateDto dto)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == GetUserId());
        if (category == null) return NotFound();

        category.Name = dto.Name;
        category.Color = dto.Color;
        category.MonthlyBudget = dto.MonthlyBudget;

        await _db.SaveChangesAsync();
        return Ok("Category updated");
    }

    /// <summary>
    /// Delete a category
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == GetUserId());
        if (category == null) return NotFound();

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return Ok("Category deleted");
    }
}