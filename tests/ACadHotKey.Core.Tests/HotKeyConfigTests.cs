using ACadHotKey.Core;
using Xunit;

namespace ACadHotKey.Core.Tests;

public class HotKeyConfigTests
{
    [Fact]
    public void Loads_the_sample_config_shipped_with_the_plugin()
    {
        var config = HotKeyConfig.Load(Path.Combine(AppContext.BaseDirectory, "ACadHotKey.config.xml"));

        Assert.True(config.Enabled);
        Assert.Equal(CommandActivePolicy.Interrupt, config.DefaultWhenCommandActive);
        Assert.Equal("_LINE", config.Find(KeyChord.Parse("1"))!.Macro);
        Assert.Contains(PromptKind.Distance, config.Find(KeyChord.Parse("6"))!.PassThroughOnPrompts);
        Assert.Equal(CommandActivePolicy.Transparent, config.Find(KeyChord.Parse("Q"))!.WhenCommandActive);
        Assert.Contains("MTEXT", config.ExcludedCommands);
    }

    [Fact]
    public void Example_from_the_specification_is_valid()
    {
        // docs/technical-specification.md, section 5.2: the first ```xml block must stay loadable.
        var spec = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "technical-specification.md"));
        var start = spec.IndexOf("```xml", StringComparison.Ordinal) + "```xml".Length;
        var xml = spec.Substring(start, spec.IndexOf("```", start, StringComparison.Ordinal) - start);

        var config = HotKeyConfig.Parse(xml);

        Assert.Equal(7, config.Bindings.Count);
        Assert.Contains("MYTEXTCMD", config.ExcludedCommands);
        Assert.Contains("MTEXT", config.ExcludedCommands);
        Assert.Equal("(c:MyLispFunc)", config.Find(KeyChord.Parse("Ctrl+Shift+D"))!.Macro);
        Assert.Equal(CommandActivePolicy.Transparent, config.Find(KeyChord.Parse("Num0"))!.WhenCommandActive);
    }

    [Fact]
    public void Reads_all_attributes()
    {
        var config = HotKeyConfig.Parse("""
            <ACadHotKey enabled="false" whenCommandActive="PassThrough" cancelSequence="^[^["
                        passThroughWhileTyping="false" ignoreDuringScript="0" echoMacro="false">
              <ExcludedCommands keepDefaults="false">MYTEXT, OTHER</ExcludedCommands>
              <DeniedWindowClasses keepDefaults="false">MyEdit</DeniedWindowClasses>
              <HotKey key="Ctrl+1" macro="(c:test)" whenCommandActive="Interrupt"
                      passThroughOnPrompts="distance Integer" description="Test" />
            </ACadHotKey>
            """);

        Assert.False(config.Enabled);
        Assert.Equal(CommandActivePolicy.PassThrough, config.DefaultWhenCommandActive);
        Assert.Equal("^[^[", config.CancelSequence);
        Assert.False(config.PassThroughWhileTyping);
        Assert.False(config.IgnoreDuringScript);
        Assert.False(config.EchoMacro);
        Assert.Equal(new[] { "MYTEXT", "OTHER" }, config.ExcludedCommands.OrderBy(x => x));
        Assert.Equal(new[] { "MyEdit" }, config.DeniedWindowClasses);
        var binding = Assert.Single(config.Bindings);
        Assert.Equal(new KeyChord('1', KeyModifiers.Ctrl), binding.Key);
        Assert.Equal(CommandActivePolicy.Interrupt, binding.WhenCommandActive);
        Assert.Equal(new[] { PromptKind.Distance, PromptKind.Integer }, binding.PassThroughOnPrompts.OrderBy(x => x));
        Assert.Equal("Test", binding.Description);
    }

    [Theory]
    [InlineData("<Other/>", "Root element")]
    [InlineData("<ACadHotKey><HotKey macro=\"_LINE\"/></ACadHotKey>", "'key'")]
    [InlineData("<ACadHotKey><HotKey key=\"1\"/></ACadHotKey>", "'macro'")]
    [InlineData("<ACadHotKey><HotKey key=\"1\" macro=\" \"/></ACadHotKey>", "'macro'")]
    [InlineData("<ACadHotKey><HotKey key=\"Foo\" macro=\"_LINE\"/></ACadHotKey>", "unknown key name")]
    [InlineData("<ACadHotKey><HotKey key=\"1\" macro=\"a\"/><HotKey key=\"1\" macro=\"b\"/></ACadHotKey>", "more than once")]
    [InlineData("<ACadHotKey whenCommandActive=\"Kill\"/>", "whenCommandActive")]
    [InlineData("<ACadHotKey enabled=\"yes\"/>", "true or false")]
    [InlineData("<ACadHotKey><HotKey key=\"1\" macro=\"a\" passThroughOnPrompts=\"Foo\"/></ACadHotKey>", "prompt kind")]
    public void Reports_readable_errors(string xml, string expectedFragment)
    {
        var ex = Assert.Throws<FormatException>(() => HotKeyConfig.Parse(xml));
        Assert.Contains(expectedFragment, ex.Message);
    }
}
