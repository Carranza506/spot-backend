using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spot.AiSearch.Api.Options;

namespace Spot.AiSearch.Api.Services.Gemini;

/// <inheritdoc cref="IGeminiClient" />
public sealed class GeminiClient(
    HttpClient httpClient,
    IOptions<GeminiOptions> options,
    ILogger<GeminiClient> logger) : IGeminiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    // Costa Rica has no daylight saving and a single fixed offset, so this avoids the
    // Windows-vs-Linux TimeZoneInfo id mismatch (e.g. "Central America Standard Time" vs
    // "America/Costa_Rica") that would otherwise bite between `dotnet run` and the container.
    private static readonly TimeSpan CostaRicaUtcOffset = TimeSpan.FromHours(-6);

    // Interpolated per call (not a const) because it needs "today" — without an explicit
    // reference date, the model has no way to resolve relative expressions like "mañana" or
    // "el viernes que viene" into an actual calendar date.
    private static string BuildSystemInstructionText(DateOnly today) =>
        $"You are a natural-language parser for a business-booking search. Today's date is " +
        $"{today:yyyy-MM-dd} (Costa Rica time). Extract the service the user is looking for, the " +
        "desired date, time, and maximum budget (in Costa Rican colones) from their query. " +
        "Resolve relative expressions (e.g. \"mañana\", \"el viernes que viene\", \"en la tarde\") " +
        "into an actual ISO-8601 date and a 24-hour time using today's date as the reference " +
        "point. You have no access to any database or external tool: only return what can be " +
        "inferred directly from the text. Use null for anything not mentioned or that cannot be " +
        "determined.";

    // Matches the fields AiSearchCriteria needs that the model can infer from text alone
    // (categoryId is resolved later, against the database, by Spot.AiSearch.Api itself).
    private static readonly object ResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            serviceQuery = new { type = "STRING", nullable = true, description = "Text of the searched service, e.g. \"corte de cabello\"." },
            date = new { type = "STRING", nullable = true, description = "ISO-8601 date (YYYY-MM-DD)." },
            time = new { type = "STRING", nullable = true, description = "24-hour time (HH:mm:ss)." },
            budgetMaxColones = new { type = "NUMBER", nullable = true, description = "Maximum budget in Costa Rican colones." },
        },
    };

    public async Task<GeminiExtractionResult> ExtractSearchCriteriaAsync(string query, CancellationToken ct = default)
    {
        var geminiOptions = options.Value;
        var stopwatch = Stopwatch.StartNew();
        var today = DateOnly.FromDateTime(DateTime.UtcNow + CostaRicaUtcOffset);

        var requestBody = new GeminiGenerateContentRequest
        {
            SystemInstruction = new GeminiContent { Parts = [new GeminiPart { Text = BuildSystemInstructionText(today) }] },
            Contents = [new GeminiContent { Role = "user", Parts = [new GeminiPart { Text = query }] }],
            GenerationConfig = new GeminiGenerationConfig { ResponseSchema = ResponseSchema },
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(geminiOptions.TimeoutSeconds));

        try
        {
            var requestUri = $"{geminiOptions.BaseUrl.TrimEnd('/')}/models/{geminiOptions.Model}:generateContent";
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, requestUri)
            {
                Content = JsonContent.Create(requestBody, options: SerializerOptions),
            };
            // Sent as a header rather than a "?key=" query parameter so the key never ends up in
            // request URLs (logs, proxies, browser history of anyone replaying the request).
            httpRequest.Headers.Add("x-goog-api-key", geminiOptions.ApiKey);

            using var response = await httpClient.SendAsync(httpRequest, timeoutCts.Token);
            stopwatch.Stop();

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = TryParseErrorMessage(body) ?? $"Gemini API returned {(int)response.StatusCode}.";
                logger.LogWarning("Gemini API error {StatusCode}: {Message}", (int)response.StatusCode, errorMessage);
                return GeminiExtractionResult.Error((int)stopwatch.ElapsedMilliseconds, "GEMINI_API_ERROR", errorMessage);
            }

            GeminiGenerateContentResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<GeminiGenerateContentResponse>(body, SerializerOptions);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Gemini API returned a non-JSON or truncated response envelope.");
                return GeminiExtractionResult.Error(
                    (int)stopwatch.ElapsedMilliseconds, "GEMINI_MALFORMED_RESPONSE", "Gemini response envelope was not valid JSON.");
            }

            var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                return GeminiExtractionResult.Error(
                    (int)stopwatch.ElapsedMilliseconds, "GEMINI_EMPTY_RESPONSE", "Gemini returned no candidates.");
            }

            JsonDocument extracted;
            try
            {
                extracted = JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Gemini returned a non-JSON response body.");
                return GeminiExtractionResult.Error(
                    (int)stopwatch.ElapsedMilliseconds, "GEMINI_INVALID_RESPONSE", "Gemini response was not valid JSON.");
            }

            return GeminiExtractionResult.Success(
                text,
                extracted,
                payload?.UsageMetadata?.PromptTokenCount,
                payload?.UsageMetadata?.CandidatesTokenCount,
                payload?.UsageMetadata?.TotalTokenCount,
                (int)stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            logger.LogWarning("Gemini API call timed out after {TimeoutSeconds}s.", geminiOptions.TimeoutSeconds);
            return GeminiExtractionResult.Timeout(
                (int)stopwatch.ElapsedMilliseconds, $"Gemini API call timed out after {geminiOptions.TimeoutSeconds}s.");
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Gemini API call failed.");
            return GeminiExtractionResult.Error((int)stopwatch.ElapsedMilliseconds, "GEMINI_REQUEST_FAILED", ex.Message);
        }
    }

    private static string? TryParseErrorMessage(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<GeminiErrorResponse>(body, SerializerOptions);
            return error?.Error?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
