using Spot.Shared.Validation;

namespace Spot.Shared.Tests.Validation;

public class PhoneNumberRulesTests
{
    [Theory]
    [InlineData("+506 8888-1234", "+50688881234")] // Costa Rica mobile
    [InlineData("+506 2260-1000", "+50622601000")] // Costa Rica landline (the seed's)
    [InlineData("  +50688881234  ", "+50688881234")]
    [InlineData("+52 55 1234 5678", "+525512345678")] // Mexico
    [InlineData("+1 (202) 555-0143", "+12025550143")] // United States
    public void ToE164_ValidNumber_ReturnsE164(string input, string expected) =>
        Assert.Equal(expected, PhoneNumberRules.ToE164(input));

    [Theory]
    [InlineData("abc")]
    [InlineData("xyz")]
    [InlineData("8888-1234")] // no country code
    [InlineData("+506 8888-123")] // one digit short for Costa Rica
    [InlineData("+506 1234-5678")] // Costa Rica has no numbers starting with 1
    [InlineData("+999 12345678")] // no such country code
    public void ToE164_InvalidNumber_ReturnsNull(string input) =>
        Assert.Null(PhoneNumberRules.ToE164(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_IsAcceptedAsOptional_ButHasNoE164(string? input)
    {
        Assert.True(PhoneNumberRules.IsBlankOrValid(input));
        Assert.Null(PhoneNumberRules.ToE164(input));
    }

    [Fact]
    public void IsBlankOrValid_RejectsInvalidNumbers() =>
        Assert.False(PhoneNumberRules.IsBlankOrValid("abc"));
}
