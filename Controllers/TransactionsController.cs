namespace BudgetBuddy.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using BudgetBuddy.Models;
using BudgetBuddy.Services;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/transactions")]
[Authorize]
public class TransactionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OcrService _ocrService;

    public TransactionsController(AppDbContext db, OcrService ocrService)
    {
        _db = db;
        _ocrService = ocrService;
    }

    private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

    /// <summary>
    /// Get all transactions for current month (paginated)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactions([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var now = DateTime.UtcNow;
        var transactions = await _db.Transactions
            .Where(t => t.UserId == GetUserId()
                        && t.TransactionDate.Year == now.Year
                        && t.TransactionDate.Month == now.Month)
            .Include(t => t.Category)
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = transactions.Select(t => new TransactionDto
        {
            Id = t.Id,
            CategoryId = t.CategoryId,
            CategoryName = t.Category?.Name,
            Amount = t.Amount,
            Currency = t.Currency,
            Description = t.Description,
            ReceiptImageUrl = t.ReceiptImageUrl,
            TransactionDate = t.TransactionDate
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Get transactions for specific month
    /// </summary>
    [HttpGet("month/{year}/{month}")]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactionsByMonth(int year, int month, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var transactions = await _db.Transactions
            .Where(t => t.UserId == GetUserId()
                        && t.TransactionDate.Year == year
                        && t.TransactionDate.Month == month)
            .Include(t => t.Category)
            .OrderByDescending(t => t.TransactionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var result = transactions.Select(t => new TransactionDto
        {
            Id = t.Id,  
            CategoryId = t.CategoryId,
            CategoryName = t.Category?.Name,
            Amount = t.Amount,
            Currency = t.Currency,
            Description = t.Description,
            ReceiptImageUrl = t.ReceiptImageUrl,
            TransactionDate = t.TransactionDate
        }).ToList();

        return Ok(result);
    }

    /// <summary>
    /// Create a new transaction (with optional OCR for receipt)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TransactionDto>> CreateTransaction(TransactionCreateDto dto)
    {
        int? categoryId = dto.CategoryId;

        Category? category = null;
        if (categoryId.HasValue)
        {
            category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId.Value && c.UserId == GetUserId());
            if (category == null) return BadRequest("Category not found");
        }

        var transaction = new Transaction
        {
            UserId = GetUserId(),
            CategoryId = categoryId,
            Amount = dto.Amount,
            Currency = dto.Currency.ToUpper(),
            Description = dto.Description
        };

        // Process OCR if receipt image provided
        if (!string.IsNullOrEmpty(dto.ReceiptImage))
        {
            try
            {
                var ocrResult = await _ocrService.ExtractFromReceiptAsync(dto.ReceiptImage);
                if (ocrResult.Amount.HasValue && transaction.Amount == 0)
                    transaction.Amount = ocrResult.Amount.Value;
                if (!string.IsNullOrEmpty(ocrResult.Description) && string.IsNullOrEmpty(transaction.Description))
                    transaction.Description = ocrResult.Description;

                // Save base64 as URL (in production, upload to cloud storage)
                transaction.ReceiptImageUrl = $"data:image/jpeg;base64,{dto.ReceiptImage.Substring(0, Math.Min(100, dto.ReceiptImage.Length))}...";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OCR failed: {ex.Message}");
            }
        }

        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetTransactions), new TransactionDto
        {
            Id = transaction.Id,
            CategoryId = transaction.CategoryId,
            CategoryName = transaction.Category?.Name,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Description = transaction.Description,
            ReceiptImageUrl = transaction.ReceiptImageUrl,
            TransactionDate = transaction.TransactionDate
        });
    }

    /// <summary>
    /// Update a transaction
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTransaction(int id, TransactionCreateDto dto)
    {
        var transaction = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == GetUserId());
        if (transaction == null) return NotFound();

        int? categoryId = dto.CategoryId;
        Category? category = null;
        if (categoryId.HasValue)
        {
            category = await _db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId.Value && c.UserId == GetUserId());
            if (category == null) return BadRequest("Category not found");
        }

        transaction.CategoryId = categoryId;
        transaction.Amount = dto.Amount;
        transaction.Currency = dto.Currency.ToUpper();
        transaction.Description = dto.Description;

        await _db.SaveChangesAsync();
        return Ok("Transaction updated");
    }

    /// <summary>
    /// Delete a transaction
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTransaction(int id)
    {
        var transaction = await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id && t.UserId == GetUserId());
        if (transaction == null) return NotFound();

        _db.Transactions.Remove(transaction);
        await _db.SaveChangesAsync();
        return Ok("Transaction deleted");
    }

    /// <summary>
    /// Extract data from receipt using OCR (preview before creating transaction)
    /// </summary>
    [HttpPost("ocr-preview")]
    public async Task<ActionResult<OcrResponseDto>> OcrPreview([FromBody] dynamic request)
    {
        var image = request?.image?.ToString();
        if (string.IsNullOrEmpty(image))
            return BadRequest("Image required");

        var result = await _ocrService.ExtractFromReceiptAsync(image);
        return Ok(result);
    }
}
