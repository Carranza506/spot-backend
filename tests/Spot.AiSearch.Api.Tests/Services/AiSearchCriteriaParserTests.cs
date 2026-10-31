using System.Text.Json;
using Spot.AiSearch.Api.Services;

namespace Spot.AiSearch.Api.Tests.Services;

public sealed class AiSearchCriteriaParserTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parse_ValidValues_AreKept()
    {
        var criteria = AiSearchCriteriaParser.Parse(Json(
            """{"serviceQuery":"corte de cabello","date":"2026-08-31","time":"14:00:00","budgetMaxColones":15000.5,"rawLocationText":"Sarapiquí"}"""));

        Assert.Equal("corte de cabello", criteria.ServiceQuery);
        Assert.Equal(new DateOnly(2026, 8, 31), criteria.Date);
        Assert.Equal(new TimeOnly(14, 0, 0), criteria.Time);
        Assert.Equal(15000.5m, criteria.BudgetMaxColones);
        Assert.Equal("Sarapiquí", criteria.RawLocationText);
    }

    [Fact]
    public void Parse_TimeWithoutSeconds_IsKept()
    {
        Assert.Equal(new TimeOnly(9, 30), AiSearchCriteriaParser.Parse(Json("""{"time":"09:30"}""")).Time);
    }

    [Fact]
    public void Parse_TextIsTrimmed_AndBlankTextIsNull()
    {
        var criteria = AiSearchCriteriaParser.Parse(Json("""{"serviceQuery":"  manicura  ","rawLocationText":"   "}"""));

        Assert.Equal("manicura", criteria.ServiceQuery);
        Assert.Null(criteria.RawLocationText);
    }

    [Theory]
    [InlineData("""{"date":"2026-13-45"}""")]
    [InlineData("""{"date":"31/08/2026"}""")]
    [InlineData("""{"date":"mañana"}""")]
    [InlineData("""{"date":20260831}""")]
    public void Parse_InvalidDate_IsNull(string json)
    {
        Assert.Null(AiSearchCriteriaParser.Parse(Json(json)).Date);
    }

    [Theory]
    [InlineData("""{"time":"25:00:00"}""")]
    [InlineData("""{"time":"14:60:00"}""")]
    [InlineData("""{"time":"2 pm"}""")]
    [InlineData("""{"time":1400}""")]
    public void Parse_InvalidTime_IsNull(string json)
    {
        Assert.Null(AiSearchCriteriaParser.Parse(Json(json)).Time);
    }

    [Theory]
    [InlineData("""{"budgetMaxColones":-1}""")]
    [InlineData("""{"budgetMaxColones":"15000"}""")]
    [InlineData("""{"budgetMaxColones":null}""")]
    [InlineData("""{"budgetMaxColones":1e400}""")]
    public void Parse_InvalidBudget_IsNull(string json)
    {
        Assert.Null(AiSearchCriteriaParser.Parse(Json(json)).BudgetMaxColones);
    }

    [Theory]
    [InlineData("""{"serviceQuery":42,"rawLocationText":true}""")]
    [InlineData("""{"serviceQuery":["corte"],"rawLocationText":{"city":"Heredia"}}""")]
    public void Parse_WrongTypes_AreNull(string json)
    {
        var criteria = AiSearchCriteriaParser.Parse(Json(json));

        Assert.Null(criteria.ServiceQuery);
        Assert.Null(criteria.RawLocationText);
    }

    [Theory]
    [InlineData("""[]""")]
    [InlineData("""null""")]
    [InlineData("\"corte\"")]
    public void Parse_NotAnObject_IsAllNull(string json)
    {
        var criteria = AiSearchCriteriaParser.Parse(Json(json));

        Assert.Equal(new Spot.AiSearch.Api.DTOs.AiSearchCriteriaDto(null, null, null, null, null, null), criteria);
    }

    [Fact]
    public void Parse_LongServiceQuery_IsCappedAt150Characters()
    {
        var longText = new string('a', 120) + " " + new string('b', 1_000_000);

        var serviceQuery = AiSearchCriteriaParser.Parse(Json($$"""{"serviceQuery":"{{longText}}"}""")).ServiceQuery;

        Assert.Equal(AiSearchCriteriaParser.ServiceQueryMaxLength, serviceQuery!.Length);
        Assert.Equal(longText[..AiSearchCriteriaParser.ServiceQueryMaxLength], serviceQuery);
    }

    [Fact]
    public void Parse_ServiceQueryOfExactlyTheMaximum_IsKeptWhole()
    {
        var text = new string('a', AiSearchCriteriaParser.ServiceQueryMaxLength);

        Assert.Equal(text, AiSearchCriteriaParser.Parse(Json($$"""{"serviceQuery":"{{text}}"}""")).ServiceQuery);
    }

    [Fact]
    public void Parse_LongRawLocationText_IsCappedAt150_WithoutSplittingAnEmoji()
    {
        var huge = new string('h', 1_000_000);
        var withEmoji = new string('h', AiSearchCriteriaParser.RawLocationTextMaxLength - 1) + "🏙";

        Assert.Equal(new string('h', AiSearchCriteriaParser.RawLocationTextMaxLength),
            AiSearchCriteriaParser.Parse(Json($$"""{"rawLocationText":"{{huge}}"}""")).RawLocationText);
        Assert.Equal(new string('h', AiSearchCriteriaParser.RawLocationTextMaxLength - 1),
            AiSearchCriteriaParser.Parse(Json($$"""{"rawLocationText":"{{withEmoji}}"}""")).RawLocationText);
    }

    [Fact]
    public void Parse_CappingNeverSplitsAnEmoji()
    {
        // 149 letters + a 2-char emoji: cutting at 150 would leave half a surrogate pair.
        var text = new string('a', AiSearchCriteriaParser.ServiceQueryMaxLength - 1) + "💇";

        Assert.Equal(new string('a', AiSearchCriteriaParser.ServiceQueryMaxLength - 1),
            AiSearchCriteriaParser.Parse(Json($$"""{"serviceQuery":"{{text}}"}""")).ServiceQuery);
    }

    [Fact]
    public void Parse_ControlCharacters_BecomeSpaces_InEveryTextField()
    {
        var criteria = AiSearchCriteriaParser.Parse(Json(
            """{"serviceQuery":"cor\u0000te\nde\tpelo","rawLocationText":"Here\u0000dia\r\n"}"""));

        Assert.Equal("cor te de pelo", criteria.ServiceQuery);
        Assert.Equal("Here dia", criteria.RawLocationText);
        Assert.DoesNotContain(criteria.ServiceQuery!, char.IsControl);
    }

    [Fact]
    public void Parse_OnlyControlCharacters_IsNull()
    {
        Assert.Null(AiSearchCriteriaParser.Parse(Json("""{"serviceQuery":"\u0000\n\t\u001f"}""")).ServiceQuery);
    }

    [Fact]
    public void Parse_CategoryId_IsNeverTakenFromTheModel()
    {
        // The model can't know real category ids; resolving one is a follow-up of #73.
        Assert.Null(AiSearchCriteriaParser.Parse(Json($$"""{"categoryId":"{{Guid.NewGuid()}}"}""")).CategoryId);
    }
}
