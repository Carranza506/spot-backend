using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spot.AiSearch.Api.Models;
using Spot.AiSearch.Api.Options;
using Spot.AiSearch.Api.Services;
using Spot.AiSearch.Api.Services.Gemini;
using Spot.AiSearch.Api.Tests.Fakes;
using Spot.AiSearch.Api.Tests.TestSupport;

namespace Spot.AiSearch.Api.Tests.Services;

public sealed class AiRequestServiceTests
{
    private const string Model = "gemini-test";

    private readonly FakeAiRequestRepository _repository = new();
    private readonly CapturingLogger<AiRequestService> _logger = new();
    private readonly AiRequestService _service;

    public AiRequestServiceTests() => _service = new AiRequestService(
        _repository, Microsoft.Extensions.Options.Options.Create(new GeminiOptions { Model = Model }), _logger);

    // ---------- RecordAsync ----------

    [Fact]
    public async Task RecordAsync_Success_PersistsOneRowWithEveryColumnMapped()
    {
        var userId = Guid.NewGuid();
        var ip = IPAddress.Parse("203.0.113.7");
        using var extracted = JsonDocument.Parse("""{"serviceQuery":"corte de cabello","budgetMaxColones":15000}""");
        var result = GeminiExtractionResult.Success(
            rawResponse: """{"candidates":[]}""", extractedParameters: extracted,
            inputTokens: 50, outputTokens: 20, totalTokens: 70, latencyMs: 812);
        var before = DateTime.UtcNow;

        var summary = await _service.RecordAsync("Busco un corte de cabello", result, userId, ip, "SpotApp/1.0");

        var row = Assert.Single(_repository.All);
        Assert.Equal(userId, row.UserId);
        Assert.Equal("gemini", row.Provider);
        Assert.Equal(Model, row.Model);
        Assert.Equal(AiRequestType.BUSINESS_SEARCH, row.RequestType);
        Assert.Equal("Busco un corte de cabello", row.Prompt);
        Assert.Equal("""{"candidates":[]}""", row.Response);
        Assert.NotNull(row.ExtractedParameters);
        Assert.Equal(extracted.RootElement.GetRawText(), row.ExtractedParameters!.RootElement.GetRawText());
        Assert.Null(row.ToolCalls);
        Assert.Equal(AiRequestStatus.SUCCESS, row.Status);
        Assert.Equal(50, row.InputTokens);
        Assert.Equal(20, row.OutputTokens);
        Assert.Equal(70, row.TotalTokens);
        Assert.Equal(812, row.LatencyMs);
        Assert.Null(row.ErrorCode);
        Assert.Null(row.ErrorMessage);
        Assert.Equal(ip, row.IpAddress);
        Assert.Equal("SpotApp/1.0", row.UserAgent);
        AssertCreatedNowInUtc(row.CreatedAt, before);

        AssertSummaryMatches(row, summary);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task RecordAsync_Success_StoresACopyOfExtractedParameters_CallerKeepsItsDocument()
    {
        var extracted = JsonDocument.Parse("""{"serviceQuery":"uñas"}""");
        var result = GeminiExtractionResult.Success("{}", extracted, 1, 1, 2, 10);

        await _service.RecordAsync("uñas", result, null, null, null);

        var row = Assert.Single(_repository.All);
        Assert.NotSame(extracted, row.ExtractedParameters);

        // The caller may dispose its own document; the stored copy stays readable.
        extracted.Dispose();
        Assert.Equal("uñas", row.ExtractedParameters!.RootElement.GetProperty("serviceQuery").GetString());
    }

    [Fact]
    public async Task RecordAsync_Error_PersistsTheRowWithErrorColumnsAndNoUsage()
    {
        var result = GeminiExtractionResult.Error(
            latencyMs: 230, errorCode: "GEMINI_API_ERROR", errorMessage: "Gemini returned 500.");
        var before = DateTime.UtcNow;

        var summary = await _service.RecordAsync("algo", result, userId: null, ipAddress: null, userAgent: null);

        var row = Assert.Single(_repository.All);
        Assert.Null(row.UserId);
        Assert.Equal("gemini", row.Provider);
        Assert.Equal(Model, row.Model);
        Assert.Equal(AiRequestType.BUSINESS_SEARCH, row.RequestType);
        Assert.Equal("algo", row.Prompt);
        Assert.Null(row.Response);
        Assert.Null(row.ExtractedParameters);
        Assert.Null(row.ToolCalls);
        Assert.Equal(AiRequestStatus.ERROR, row.Status);
        Assert.Null(row.InputTokens);
        Assert.Null(row.OutputTokens);
        Assert.Null(row.TotalTokens);
        Assert.Equal(230, row.LatencyMs);
        Assert.Equal("GEMINI_API_ERROR", row.ErrorCode);
        Assert.Equal("Gemini returned 500.", row.ErrorMessage);
        Assert.Null(row.IpAddress);
        Assert.Null(row.UserAgent);
        AssertCreatedNowInUtc(row.CreatedAt, before);

        AssertSummaryMatches(row, summary);
    }

    [Fact]
    public async Task RecordAsync_Timeout_PersistsTheRowWithTimeoutColumnsAndNoUsage()
    {
        var userId = Guid.NewGuid();
        var ip = IPAddress.Parse("2001:db8::1");
        var result = GeminiExtractionResult.Timeout(latencyMs: 10000, errorMessage: "Gemini did not answer in 10 s.");
        var before = DateTime.UtcNow;

        var summary = await _service.RecordAsync("barbería cerca", result, userId, ip, "Mozilla/5.0");

        var row = Assert.Single(_repository.All);
        Assert.Equal(userId, row.UserId);
        Assert.Equal("gemini", row.Provider);
        Assert.Equal(Model, row.Model);
        Assert.Equal(AiRequestType.BUSINESS_SEARCH, row.RequestType);
        Assert.Equal("barbería cerca", row.Prompt);
        Assert.Null(row.Response);
        Assert.Null(row.ExtractedParameters);
        Assert.Null(row.ToolCalls);
        Assert.Equal(AiRequestStatus.TIMEOUT, row.Status);
        Assert.Null(row.InputTokens);
        Assert.Null(row.OutputTokens);
        Assert.Null(row.TotalTokens);
        Assert.Equal(10000, row.LatencyMs);
        Assert.Equal("GEMINI_TIMEOUT", row.ErrorCode);
        Assert.Equal("Gemini did not answer in 10 s.", row.ErrorMessage);
        Assert.Equal(ip, row.IpAddress);
        Assert.Equal("Mozilla/5.0", row.UserAgent);
        AssertCreatedNowInUtc(row.CreatedAt, before);

        AssertSummaryMatches(row, summary);
    }

    [Fact]
    public async Task RecordAsync_InsertsWithANonCancelableToken_NotTheRequests()
    {
        var result = GeminiExtractionResult.Timeout(10000, "timeout");

        await _service.RecordAsync("algo", result, null, null, null);

        // A client disconnect cancels the request token; the insert must not be tied to it.
        Assert.Equal(CancellationToken.None, _repository.LastAddToken);
        Assert.False(_repository.LastAddToken!.Value.CanBeCanceled);
    }

    [Fact]
    public async Task RecordAsync_DbFailure_IsLoggedAtErrorWithStatusAndErrorCode_AndRethrown()
    {
        var dbError = new DbUpdateException("insert failed");
        _repository.FailAddWith = dbError;
        var result = GeminiExtractionResult.Timeout(10000, "timeout");

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(
            () => _service.RecordAsync("algo", result, null, null, null));

        Assert.Same(dbError, thrown);
        Assert.Empty(_repository.All);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Same(dbError, entry.Exception);
        Assert.Contains("TIMEOUT", entry.Message);
        Assert.Contains("GEMINI_TIMEOUT", entry.Message);
    }

    private static void AssertCreatedNowInUtc(DateTime createdAt, DateTime before)
    {
        Assert.Equal(DateTimeKind.Utc, createdAt.Kind);
        Assert.InRange(createdAt, before, DateTime.UtcNow);
    }

    private static void AssertSummaryMatches(AiRequest row, Spot.AiSearch.Api.DTOs.AiRequestSummaryDto summary)
    {
        Assert.NotEqual(Guid.Empty, summary.Id);
        Assert.Equal(row.Id, summary.Id);
        Assert.Equal(row.Status.ToString(), summary.Status);
        Assert.Equal("BUSINESS_SEARCH", summary.RequestType);
        Assert.Equal(row.TotalTokens, summary.TotalTokens);
        Assert.Equal(row.LatencyMs, summary.LatencyMs);
        Assert.Equal(row.CreatedAt, summary.CreatedAt);
    }
}
