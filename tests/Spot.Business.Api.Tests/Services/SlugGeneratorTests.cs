using Spot.Business.Api.Services;

namespace Spot.Business.Api.Tests.Services;

public sealed class SlugGeneratorTests
{
    [Theory]
    [InlineData("Salón Bella Vista", "salon-bella-vista")]
    [InlineData("  Peluquería Ñandú & Co.  ", "peluqueria-nandu-co")]
    [InlineData("Barber--Shop 24/7", "barber-shop-24-7")]
    [InlineData("!!!", SlugGenerator.Fallback)]
    public void FromName_ProducesUrlSafeSlug(string name, string expected) =>
        Assert.Equal(expected, SlugGenerator.FromName(name));

    [Fact]
    public void FromName_LongName_IsCappedAtMaxLength()
    {
        var slug = SlugGenerator.FromName(new string('a', 300));

        Assert.Equal(SlugGenerator.MaxLength, slug.Length);
    }

    [Fact]
    public void WithSuffix_KeepsResultWithinMaxLength()
    {
        var baseSlug = new string('a', SlugGenerator.MaxLength);

        var slug = SlugGenerator.WithSuffix(baseSlug, 12);

        Assert.Equal(SlugGenerator.MaxLength, slug.Length);
        Assert.EndsWith("-12", slug);
    }

    [Fact]
    public void WithSuffix_FirstCandidateIsTheBaseItself() =>
        Assert.Equal("bella", SlugGenerator.WithSuffix("bella", 1));
}
