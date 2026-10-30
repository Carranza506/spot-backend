using System.Text.Json;
using Spot.AiSearch.Api.Services;
using Spot.AiSearch.Api.Services.Gemini;

namespace Spot.AiSearch.Api.Tests.Services;

public sealed class AuditStorageTests
{
    [Fact]
    public void WithoutNul_CleansStringsAndKeysAtEveryLevel_AndKeepsOtherValues()
    {
        using var original = JsonDocument.Parse(
            """{"a\u0000":"x\u0000y","list":["\u0000",{"deep":"z\u0000"},2.5,false,null]}""");
        var result = GeminiExtractionResult.Success("raw", original, 1, 2, 3, 4);

        var storable = AuditStorage.WithoutNul(result);
        using var cleaned = storable.ExtractedParameters!;

        var root = cleaned.RootElement;
        Assert.DoesNotContain("\\u0000", root.GetRawText());
        Assert.Equal("x y", root.GetProperty("a ").GetString());
        Assert.Equal(" ", root.GetProperty("list")[0].GetString());
        Assert.Equal("z ", root.GetProperty("list")[1].GetProperty("deep").GetString());
        Assert.Equal(2.5, root.GetProperty("list")[2].GetDouble());
        Assert.False(root.GetProperty("list")[3].GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("list")[4].ValueKind);
        // The caller's document is a separate one, left untouched.
        Assert.Contains("\\u0000", original.RootElement.GetRawText());
    }

    [Fact]
    public void WithoutNul_AnEscapedBackslashBeforeU0000_IsNotANul_AndStays()
    {
        // The JSON text \\u0000 is a backslash followed by "u0000": a legitimate string, not a NUL.
        using var original = JsonDocument.Parse("""{"path":"C:\\u0000"}""");

        using var cleaned = AuditStorage.WithoutNul(GeminiExtractionResult.Success("raw", original, null, null, null, 1))
            .ExtractedParameters!;

        Assert.Equal(@"C:\u0000", cleaned.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public void WithoutNul_RepeatedKeys_AreKeptInOrder_AndCleaned()
    {
        using var original = JsonDocument.Parse("""{"k":"a\u0000","k":{"k":1,"k":"b\u0000"}}""");

        using var cleaned = AuditStorage.WithoutNul(GeminiExtractionResult.Success("raw", original, null, null, null, 1))
            .ExtractedParameters!;

        Assert.Equal("""{"k":"a ","k":{"k":1,"k":"b "}}""", cleaned.RootElement.GetRawText());
    }

    [Fact]
    public void WithoutNul_TextFields_LoseTheirNul()
    {
        var error = AuditStorage.WithoutNul(GeminiExtractionResult.Error(10, "GEMINI_API_ERROR", "bad\0message"));
        var success = AuditStorage.WithoutNul(
            GeminiExtractionResult.Success("cor\0te", JsonDocument.Parse("{}"), null, null, null, 1));

        Assert.Equal("bad message", error.ErrorMessage);
        Assert.Null(error.ExtractedParameters);
        Assert.Equal("cor te", success.RawResponse);
        success.ExtractedParameters!.Dispose();
    }
}
