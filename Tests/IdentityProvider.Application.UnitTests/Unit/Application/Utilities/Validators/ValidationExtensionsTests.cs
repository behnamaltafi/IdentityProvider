using IdentityProvider.Application.Validators;

namespace IdentityProvider.Application.UnitTests.Validators;

public sealed class ValidationExtensionsTests
{
    [Theory]
    [InlineData("{\"title\":\"ظپط±ظ…\"}", true)]
    [InlineData("[1,2,3]", true)]
    [InlineData("null", true)]
    [InlineData("{\"nested\":[true,null,42]}", true)]
    [InlineData("{\"trailing\":true,}", false)]
    [InlineData("{invalid-json}", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidJson_WhenInputIsString_ReturnsExpectedResult(string input, bool expected)
    {
        Assert.Equal(expected, input.IsValidJson());
    }

    [Fact]
    public void IsValidJson_WhenDynamicObjectRendersJson_ReturnsTrue()
    {
        dynamic input = new JsonLike("{\"id\":1}");

        var result = JsonValidationExtension.IsValidJson(input);

        Assert.True(result);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("not-base64", false)]
    public void IsValidPng_WhenInputIsEmptyOrInvalid_ReturnsExpectedResult(string? input, bool expected)
    {
        Assert.Equal(expected, input!.IsValidPng());
    }

    [Fact]
    public void IsValidPng_WhenBase64ContainsPngSignature_ReturnsTrue()
    {
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };

        Assert.True(Convert.ToBase64String(pngHeader).IsValidPng());
    }

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x00 })]
    public void IsValidPng_WhenBytesDoNotContainPngSignature_ReturnsFalse(byte[] bytes)
    {
        Assert.False(Convert.ToBase64String(bytes).IsValidPng());
    }

    private sealed class JsonLike(string value)
    {
        public override string ToString() => value;
    }
}
