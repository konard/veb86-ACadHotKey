using ACadHotKey.Core;
using Xunit;

namespace ACadHotKey.Core.Tests;

public class MacroTextTests
{
    [Theory]
    [InlineData("^C^c_LINE", "\x03\x03_LINE\n")]
    [InlineData("^[^[_LINE", "\x1B\x1B_LINE\n")]
    [InlineData("_LINE\\n", "_LINE\n")]
    [InlineData("_LINE ", "_LINE ")]
    [InlineData("_LINE\r", "_LINE\r")]
    [InlineData("_LINE\n", "_LINE\n")]
    [InlineData("(c:MyFunc)", "(c:MyFunc)\n")]
    public void Macros_expand_supported_escapes_and_preserve_existing_enter(string macro, string expected)
        => Assert.Equal(expected, MacroText.Build(macro));

    [Theory]
    [InlineData("_ZOOM _E", "'_ZOOM _E\n")]
    [InlineData("'_ZOOM _E", "'_ZOOM _E\n")]
    [InlineData("(c:MyFunc)", "(c:MyFunc)\n")]
    public void Transparent_commands_do_not_duplicate_prefixes(string macro, string expected)
        => Assert.Equal(expected, MacroText.BuildTransparent(macro));
}
