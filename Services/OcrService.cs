namespace BudgetBuddy.Services;

using BudgetBuddy.Models;
using System.Net.Http.Json;
using System.Text.Json;

public class OcrService
{
    private readonly HttpClient _http;
    private readonly ILogger<OcrService> _logger;
    private readonly IConfiguration _config;

    public OcrService(HttpClient http, ILogger<OcrService> logger, IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _config = config;
    }

    public async Task<OcrResponseDto> ExtractFromReceiptAsync(string base64Image)
    {
        var response = new OcrResponseDto();

        try
        {
            var apiKey = _config["GOOGLE_VISION_API_KEY"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Google Vision API key not configured");
                return response;
            }

            var payload = new
            {
                requests = new[] {
                    new {
                        image = new { content = base64Image },
                        features = new[] { new { type = "TEXT_DETECTION" } }
                    }
                }
            };

            var result = await _http.PostAsJsonAsync(
                $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}",
                payload
            );

            if (result.IsSuccessStatusCode)
            {
                // ✅ Nouvelle méthode moderne (et importée via System.Net.Http.Json)
                var content = await result.Content.ReadFromJsonAsync<JsonElement>();

                if (content.TryGetProperty("responses", out var responses) &&
                    responses[0].TryGetProperty("textAnnotations", out var annotations) &&
                    annotations[0].TryGetProperty("description", out var description))
                {
                    var text = description.GetString() ?? "";

                    response.RawText = text;
                    response.Amount = ExtractAmount(text);
                    response.Description = ExtractDescription(text);
                }
            }
            else
            {
                _logger.LogWarning($"Google Vision API returned {result.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"OCR error: {ex.Message}");
        }

        return response;
    }

    private decimal? ExtractAmount(string text)
    {
        var matches = System.Text.RegularExpressions.Regex.Matches(text, @"[\d.,]+(?:[.,]\d{2})?");
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var numStr = match.Value.Replace(",", ".");
            if (decimal.TryParse(numStr, out var amount) && amount > 0 && amount < 10000)
                return amount;
        }
        return null;
    }

    private string ExtractDescription(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 0 ? lines[0][..Math.Min(100, lines[0].Length)] : "Receipt";
    }
}
