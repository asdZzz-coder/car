using car.Services;

namespace car.Tests
{
    /// <summary>數字與日期輸入的自動測試（千分位、全形數字、各種日期寫法）。</summary>
    public class TextFormatTests
    {
        [Theory]
        [InlineData("12345", 12345)]
        [InlineData("12,345", 12345)]
        [InlineData(" 38.5 ", 38.5)]
        [InlineData("１２，３４５", 12345)]     // 全形數字與逗號
        [InlineData("３８．５", 38.5)]
        [InlineData("38。5", 38.5)]
        public void TryParseDouble_AcceptsCommonInput(string text, double expected)
        {
            Assert.True(TextFormat.TryParseDouble(text, out var v));
            Assert.Equal(expected, v);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("1.2.3")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void TryParseDouble_RejectsGarbage(string? text) =>
            Assert.False(TextFormat.TryParseDouble(text, out _));

        [Theory]
        [InlineData("1,200", 1200)]
        [InlineData("１２００", 1200)]
        [InlineData("1199.5", 1199.5)]
        public void TryParseDecimal_AcceptsCommonInput(string text, decimal expected)
        {
            Assert.True(TextFormat.TryParseDecimal(text, out var v));
            Assert.Equal(expected, v);
        }

        [Theory]
        [InlineData("2026/10/06")]
        [InlineData("2026/10/6")]
        [InlineData("2026-10-06")]
        [InlineData("2026.10.6")]
        [InlineData("20261006")]
        [InlineData("２０２６／１０／０６")]
        public void TryParseDate_AcceptsCommonFormats(string text)
        {
            Assert.True(TextFormat.TryParseDate(text, out var d));
            Assert.Equal(new DateTime(2026, 10, 6), d);
        }

        [Theory]
        [InlineData("2026/13/01")]
        [InlineData("10/06")]
        [InlineData("昨天")]
        [InlineData("")]
        public void TryParseDate_RejectsInvalid(string text) =>
            Assert.False(TextFormat.TryParseDate(text, out _));

        [Fact]
        public void Formats()
        {
            Assert.Equal("2026/10/06", TextFormat.Date(new DateTime(2026, 10, 6)));
            Assert.Equal("12,345", TextFormat.Km(12345));
            Assert.Equal("12,345.6", TextFormat.Km(12345.6));
            Assert.Equal("38.52", TextFormat.Liters(38.523));
            Assert.Equal("1,200", TextFormat.Money(1200m));
            Assert.Equal("12.50", TextFormat.Rate(12.5));
            Assert.Equal("12345.6", TextFormat.Plain(12345.6));
        }
    }
}
