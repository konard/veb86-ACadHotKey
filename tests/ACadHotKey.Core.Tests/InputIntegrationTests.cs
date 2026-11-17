using ACadHotKey.Core;
using Xunit;

namespace ACadHotKey.Core.Tests;

public class InputIntegrationTests
{
    private static HotKeyEngine Engine() => new(HotKeyConfig.Parse("""
        <ACadHotKey><HotKey key="1" macro="_LINE"/></ACadHotKey>
        """));

    [Fact]
    public void Failed_dispatch_passes_original_key_and_does_not_swallow_release()
    {
        var engine = Engine();
        var state = new EditorState { DrawingHasFocus = true };
        Exception? logged = null;
        var decision = KeyMessageProcessor.Process(engine, KeyboardMessage.KeyDown('1'), state,
            _ => throw new InvalidOperationException("host rejected command"), error => logged = error);

        Assert.False(decision.Handled);
        Assert.Equal("host rejected command", logged!.Message);
        Assert.False(engine.Process(KeyboardMessage.KeyUp('1'), state).Handled);
        Assert.False(engine.Process(KeyboardMessage.KeyDown('1', isRepeat: true), state).Handled);
    }

    [Fact]
    public void Logger_failure_cannot_escape_dispatch()
    {
        Assert.False(KeyMessageProcessor.Process(Engine(), KeyboardMessage.KeyDown('1'),
            new EditorState { DrawingHasFocus = true }, _ => throw new IOException(), _ => throw new IOException()).Handled);
    }

    [Fact]
    public void Keydown_repeat_release_queues_exactly_one_cancelled_macro()
    {
        var engine = Engine();
        var state = new EditorState { DrawingHasFocus = true, CommandNames = "CIRCLE", CmdActive = 1 };
        var sent = new List<string>();
        foreach (var key in new[] { KeyboardMessage.KeyDown('1'), KeyboardMessage.KeyDown('1', isRepeat: true), KeyboardMessage.KeyUp('1') })
            Assert.True(KeyMessageProcessor.Process(engine, key, state, sent.Add, _ => Assert.Fail("unexpected dispatch error")).Handled);
        Assert.Equal("\x03\x03_LINE\n", Assert.Single(sent));
    }

    [Theory]
    [InlineData("AcDrawing", true, true)]
    [InlineData("RichEdit50W", true, false)]
    [InlineData("AcDrawing", false, false)]
    [InlineData("", true, false)]
    public void Focus_requires_actual_focused_document_and_checks_control_ancestors(string parentClass, bool focus, bool expected)
    {
        var target = new IntPtr(1);
        var parent = new IntPtr(2);
        var doc = new IntPtr(3);
        Assert.Equal(expected, FocusPolicy.IsDrawingArea(target, doc, focus ? target : IntPtr.Zero,
            w => w == target ? parent : w == parent ? doc : IntPtr.Zero,
            w => w == parent ? parentClass : "AcDrawing", HotKeyConfig.DefaultDeniedWindowClasses));
    }

    [Fact]
    public void Cyclic_or_unrelated_window_ancestry_is_rejected()
    {
        Assert.False(FocusPolicy.IsDrawingArea(new(1), new(3), new(1), _ => new(1), _ => "AcDrawing", Array.Empty<string>()));
        Assert.False(FocusPolicy.IsDrawingArea(new(1), new(3), new(1), _ => IntPtr.Zero, _ => "AcDrawing", Array.Empty<string>()));
    }

    [Fact]
    public void Reload_is_atomic_and_does_not_fall_back_from_invalid_user_config()
    {
        var dir = Path.Combine(Path.GetTempPath(), "achk-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var user = Path.Combine(dir, "user.xml");
            var shipped = Path.Combine(dir, "shipped.xml");
            File.WriteAllText(shipped, "<ACadHotKey><HotKey key='1' macro='_LINE'/></ACadHotKey>");
            var store = new HotKeyConfigStore();
            Assert.True(store.TryReload(new[] { user, shipped }, out _));
            var previous = store.Current;
            File.WriteAllText(user, "<ACadHotKey><HotKey key='2' macro='_CIRCLE'/><HotKey key='3'/></ACadHotKey>");
            Assert.False(store.TryReload(new[] { user, shipped }, out var error));
            Assert.Contains(user, error);
            Assert.Contains("macro", error);
            Assert.Same(previous, store.Current);
            Assert.Equal(shipped, store.Path);
            File.WriteAllText(user, "<ACadHotKey enabled='false'><HotKey key='2' macro='_CIRCLE'/></ACadHotKey>");
            Assert.True(store.TryReload(new[] { user, shipped }, out _));
            Assert.False(store.Current.Enabled);
            Assert.Equal(user, store.Path);
            Assert.Equal("_CIRCLE", store.Current.Find(KeyChord.Parse("2"))!.Macro);
            File.Delete(user);
            File.Delete(shipped);
            previous = store.Current;
            Assert.False(store.TryReload(new[] { user, shipped }, out _));
            Assert.Same(previous, store.Current);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Binding_index_tracks_replacement_removal_and_clear()
    {
        var config = new HotKeyConfig();
        config.Bindings.Add(new(KeyChord.Parse("1"), "_LINE"));
        config.Bindings[0] = new(KeyChord.Parse("2"), "_CIRCLE");
        Assert.Null(config.Find(KeyChord.Parse("1")));
        Assert.Equal("_CIRCLE", config.Find(KeyChord.Parse("2"))!.Macro);
        config.Bindings.RemoveAt(0);
        Assert.Null(config.Find(KeyChord.Parse("2")));
        config.Bindings.Add(new(KeyChord.Parse("1"), "_LINE"));
        config.Bindings.Clear();
        Assert.Null(config.Find(KeyChord.Parse("1")));
    }
}
