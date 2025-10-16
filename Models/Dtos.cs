using System.ComponentModel.DataAnnotations;
namespace BudgetBuddy.Models;

public class RegisterDto
{
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}

public class UserUpdateDto
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? PreferredCurrency { get; set; }
}

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public decimal MonthlyBudget { get; set; }
    public decimal SpentThisMonth { get; set; }
}

public class CategoryCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#000000";
    public decimal MonthlyBudget { get; set; }
}

public class TransactionDto
{
    public int Id { get; set; }
    public int? CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? ReceiptImageUrl { get; set; }
    public DateTime TransactionDate { get; set; }
}

public class TransactionCreateDto
{
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string Description { get; set; } = string.Empty;
    public string? ReceiptImage { get; set; } // Base64 image for OCR
}

public class OcrResponseDto
{
    public decimal? Amount { get; set; }
    public string? Description { get; set; }
    public string? RawText { get; set; }
}

public class StatsDto
{
    public decimal TotalSpentThisMonth { get; set; }
    public decimal TotalBudgetThisMonth { get; set; }
    public List<CategorySpendingDto> ByCategory { get; set; } = new();
    public List<CurrencySpendingDto> ByCurrency { get; set; } = new();
}

public class CategorySpendingDto
{
    public string CategoryName { get; set; } = string.Empty;
    public decimal Spent { get; set; }
    public decimal Budget { get; set; }
}

public class CurrencySpendingDto
{
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal ConvertedToPreferred { get; set; }
}
