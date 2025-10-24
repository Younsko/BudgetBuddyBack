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
    private readonly CategoryService _categoryService;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        AppDbContext db, 
        OcrService ocrService, 
        CategoryService categoryService,
        ILogger<TransactionsController> logger)
    {
        _db = db;
        _ocrService = ocrService;
        _categoryService = categoryService;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(User.FindFirst("id")?.Value ?? "0");

    [HttpGet]
    [ProducesResponseType(typeof(List<TransactionDto>), 200)]
    public async Task<ActionResult<object>> GetTransactions(
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 20;

        var userId = GetUserId();
        var now = DateTime.UtcNow;

        try
        {
            var query = _db.Transactions
                .Where(t => t.UserId == userId
                            && t.TransactionDate.Year == now.Year
                            && t.TransactionDate.Month == now.Month)
                .Include(t => t.Category);

            var total = await query.CountAsync();

            var transactions = await query
                .OrderByDescending(t => t.TransactionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TransactionDto
                {
                    Id = t.Id,
                    CategoryId = t.CategoryId,
                    CategoryName = t.Category != null ? t.Category.Name : null,
                    CategoryColor = t.Category != null ? t.Category.Color : null,
                    Amount = t.Amount,
                    Currency = t.Currency,
                    Description = t.Description,
                    ReceiptImageUrl = t.ReceiptImageUrl,
                    TransactionDate = t.TransactionDate,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                data = transactions,
                pagination = new
                {
                    page,
                    pageSize,
                    total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Get transactions error: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while fetching transactions" });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(TransactionDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TransactionDto>> GetTransaction(int id)
    {
        var userId = GetUserId();

        var transaction = await _db.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (transaction == null)
            return NotFound(new { error = "Transaction not found" });

        if (transaction.UserId != userId)
            return Forbid();

        return Ok(new TransactionDto
        {
            Id = transaction.Id,
            CategoryId = transaction.CategoryId,
            CategoryName = transaction.Category?.Name,
            CategoryColor = transaction.Category?.Color,
            Amount = transaction.Amount,
            Currency = transaction.Currency,
            Description = transaction.Description,
            ReceiptImageUrl = transaction.ReceiptImageUrl,
            TransactionDate = transaction.TransactionDate,
            CreatedAt = transaction.CreatedAt
        });
    }

    [HttpPost]
    [ProducesResponseType(typeof(TransactionDto), 201)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<TransactionDto>> CreateTransaction(TransactionCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var userId = GetUserId();
        try
        {
            if (dto.CategoryId.HasValue && dto.CategoryId.Value > 0)
            {
                if (!await _categoryService.UserOwnsCategoryAsync(userId, dto.CategoryId.Value))
                    return BadRequest(new { error = "Category not found or access denied" });
            }

            // Parse la date reçue (qui devrait être en ISO format)
            DateTime transactionDate = DateTime.UtcNow;
            if (dto.Date.HasValue)
            {
                transactionDate = dto.Date.Value.ToUniversalTime();
            }
            else if (!string.IsNullOrEmpty(dto.DateString))
            {
                if (DateTime.TryParse(dto.DateString, out var parsedDate))
                {
                    transactionDate = parsedDate.ToUniversalTime();
                }
            }

            var transaction = new Transaction
            {
                UserId = userId,
                CategoryId = dto.CategoryId.HasValue && dto.CategoryId.Value > 0 ? dto.CategoryId.Value : null,
                Amount = dto.Amount,
                Currency = dto.Currency.ToUpper(),
                Description = dto.Description,
                TransactionDate = transactionDate,
                CreatedAt = DateTime.UtcNow
            };

            if (!string.IsNullOrEmpty(dto.ReceiptImage))
            {
                try
                {
                    var ocrResult = await _ocrService.ExtractFromReceiptAsync(dto.ReceiptImage);
                    
                    if (ocrResult.Amount.HasValue && transaction.Amount == 0)
                        transaction.Amount = ocrResult.Amount.Value;
                    
                    if (!string.IsNullOrEmpty(ocrResult.Description) && string.IsNullOrEmpty(transaction.Description))
                        transaction.Description = ocrResult.Description;

                    transaction.ReceiptImageUrl = $"data:image/jpeg;base64,{dto.ReceiptImage.Substring(0, Math.Min(100, dto.ReceiptImage.Length))}...";
                    
                    _logger.LogInformation($"OCR processed for transaction by user {userId}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"OCR failed: {ex.Message}");
                }
            }

            _db.Transactions.Add(transaction);
            await _db.SaveChangesAsync();

            await _db.Entry(transaction).Reference(t => t.Category).LoadAsync();

            _logger.LogInformation($"Transaction created (ID: {transaction.Id}) by user {userId} with date {transaction.TransactionDate}");

            return CreatedAtAction(nameof(GetTransaction), new { id = transaction.Id }, new TransactionDto
            {
                Id = transaction.Id,
                CategoryId = transaction.CategoryId,
                CategoryName = transaction.Category?.Name,
                CategoryColor = transaction.Category?.Color,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Description = transaction.Description,
                ReceiptImageUrl = transaction.ReceiptImageUrl,
                TransactionDate = transaction.TransactionDate,
                CreatedAt = transaction.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Create transaction error: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while creating transaction" });
        }
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(TransactionDto), 200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<ActionResult<TransactionDto>> UpdateTransaction(int id, TransactionCreateDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);
        
        var userId = GetUserId();
        var transaction = await _db.Transactions
            .Include(t => t.Category)
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);
        
        if (transaction == null)
            return NotFound(new { error = "Transaction not found or access denied" });

        try
        {
            if (dto.CategoryId.HasValue && dto.CategoryId.Value > 0)
            {
                if (!await _categoryService.UserOwnsCategoryAsync(userId, dto.CategoryId.Value))
                    return BadRequest(new { error = "Category not found or access denied" });
            }

            transaction.CategoryId = dto.CategoryId.HasValue && dto.CategoryId.Value > 0 ? dto.CategoryId.Value : null;
            transaction.Amount = dto.Amount;
            transaction.Currency = dto.Currency.ToUpper();
            transaction.Description = dto.Description;

            // Update la date si fournie
            if (dto.Date.HasValue)
            {
                transaction.TransactionDate = dto.Date.Value.ToUniversalTime();
            }
            else if (!string.IsNullOrEmpty(dto.DateString))
            {
                if (DateTime.TryParse(dto.DateString, out var parsedDate))
                {
                    transaction.TransactionDate = parsedDate.ToUniversalTime();
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation($"Transaction updated (ID: {id}) by user {userId} with date {transaction.TransactionDate}");
            
            return Ok(new TransactionDto
            {
                Id = transaction.Id,
                CategoryId = transaction.CategoryId,
                CategoryName = transaction.Category?.Name,
                CategoryColor = transaction.Category?.Color,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Description = transaction.Description,
                ReceiptImageUrl = transaction.ReceiptImageUrl,
                TransactionDate = transaction.TransactionDate,
                CreatedAt = transaction.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Update transaction error: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while updating transaction" });
        }
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(403)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteTransaction(int id)
    {
        var userId = GetUserId();

        var transaction = await _db.Transactions
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (transaction == null)
            return NotFound(new { error = "Transaction not found or access denied" });

        try
        {
            _db.Transactions.Remove(transaction);
            await _db.SaveChangesAsync();

            _logger.LogInformation($"Transaction deleted (ID: {id}) by user {userId}");

            return Ok(new { message = "Transaction deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Delete transaction error: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while deleting transaction" });
        }
    }

    [HttpGet("month/{year}/{month}")]
    [ProducesResponseType(typeof(List<TransactionDto>), 200)]
    public async Task<ActionResult<object>> GetTransactionsByMonth(
        int year, 
        int month, 
        [FromQuery] int page = 1, 
        [FromQuery] int pageSize = 20)
    {
        if (year < 2000 || year > 2100) 
            return BadRequest(new { error = "Invalid year" });
        if (month < 1 || month > 12) 
            return BadRequest(new { error = "Invalid month" });

        var userId = GetUserId();

        try
        {
            var query = _db.Transactions
                .Where(t => t.UserId == userId
                            && t.TransactionDate.Year == year
                            && t.TransactionDate.Month == month)
                .Include(t => t.Category);

            var total = await query.CountAsync();

            var transactions = await query
                .OrderByDescending(t => t.TransactionDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TransactionDto
                {
                    Id = t.Id,
                    CategoryId = t.CategoryId,
                    CategoryName = t.Category != null ? t.Category.Name : null,
                    CategoryColor = t.Category != null ? t.Category.Color : null,
                    Amount = t.Amount,
                    Currency = t.Currency,
                    Description = t.Description,
                    ReceiptImageUrl = t.ReceiptImageUrl,
                    TransactionDate = t.TransactionDate,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                data = transactions,
                pagination = new
                {
                    page,
                    pageSize,
                    total,
                    totalPages = (int)Math.Ceiling(total / (double)pageSize)
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Get transactions by month error: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred while fetching transactions" });
        }
    }

    [HttpPost("ocr-preview")]
    [ProducesResponseType(typeof(OcrResponseDto), 200)]
    [ProducesResponseType(400)]
    public async Task<ActionResult<OcrResponseDto>> OcrPreview([FromBody] OcrPreviewRequest request)
    {
        if (string.IsNullOrEmpty(request.Image))
            return BadRequest(new { error = "Image required" });

        try
        {
            var result = await _ocrService.ExtractFromReceiptAsync(request.Image);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError($"OCR preview error: {ex.Message}");
            return StatusCode(500, new { error = "An error occurred during OCR processing" });
        }
    }
}

public class OcrPreviewRequest
{
    public string Image { get; set; } = string.Empty;
}