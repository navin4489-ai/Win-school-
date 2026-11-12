using SchoolErp.Application.Exams;
using Xunit;

namespace SchoolErp.Tests;

public sealed class GradingCalculatorTests
{
    [Theory]
    [InlineData(95, "A1")]
    [InlineData(85, "A2")]
    [InlineData(75, "B1")]
    [InlineData(65, "B2")]
    [InlineData(55, "C1")]
    [InlineData(45, "C2")]
    [InlineData(35, "D")]
    [InlineData(20, "E")]
    public void Grade_UsesCbseScale(decimal percentage, string expected) =>
        Assert.Equal(expected, GradingCalculator.Grade(percentage));

    [Fact]
    public void Grade_BoundariesAreInclusive()
    {
        Assert.Equal("A1", GradingCalculator.Grade(91));
        Assert.Equal("D", GradingCalculator.Grade(33));
        Assert.Equal("E", GradingCalculator.Grade(32.99m));
    }
}
