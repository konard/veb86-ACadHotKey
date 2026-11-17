using ACadHotKey.Core;
using Xunit;

namespace ACadHotKey.Core.Tests;

/// <summary>
/// Scenarios from docs/technical-specification.md, section 4 (behaviour rules) and section 8 (acceptance tests).
/// </summary>
public class HotKeyEngineTests
{
    private const int Key1 = '1';
    private const int KeyL = 'L';

    private static HotKeyEngine CreateEngine(Action<HotKeyConfig>? configure = null)
    {
        var config = new HotKeyConfig();
        config.Bindings.Add(new HotKeyBinding(KeyChord.Parse("1"), "_LINE"));
        config.Bindings.Add(new HotKeyBinding(KeyChord.Parse("2"), "_CIRCLE") { WhenCommandActive = CommandActivePolicy.PassThrough });
        config.Bindings.Add(new HotKeyBinding(KeyChord.Parse("Q"), "_ZOOM _E") { WhenCommandActive = CommandActivePolicy.Transparent });
        config.Bindings.Add(new HotKeyBinding(KeyChord.Parse("Ctrl+3"), "(c:my3)"));
        var offset = new HotKeyBinding(KeyChord.Parse("6"), "_OFFSET");
        offset.PassThroughOnPrompts.Add(PromptKind.Distance);
        config.Bindings.Add(offset);
        configure?.Invoke(config);
        return new HotKeyEngine(config);
    }

    private static EditorState Idle() => new() { DrawingHasFocus = true };

    private static EditorState InCommand(string names = "LINE", int cmdActive = 1, PromptKind prompt = PromptKind.Point) =>
        new() { DrawingHasFocus = true, CommandNames = names, CmdActive = cmdActive, Prompt = prompt };

    [Fact]
    public void Idle_hotkey_runs_macro_without_cancel_prefix()
    {
        var d = CreateEngine().Process(KeyboardMessage.KeyDown(Key1), Idle());

        Assert.Equal(KeyAction.Execute, d.Action);
        Assert.Equal("_LINE\n", d.Text);
        Assert.True(d.Handled);
    }

    [Fact]
    public void Active_command_is_interrupted_with_cancel_sequence_then_macro_runs()
    {
        var d = CreateEngine().Process(KeyboardMessage.KeyDown(Key1), InCommand("CIRCLE"));

        Assert.Equal(KeyAction.Execute, d.Action);
        Assert.Equal("\x03\x03_LINE\n", d.Text);
    }

    [Fact]
    public void Lisp_only_activity_counts_as_active_command()
    {
        var d = CreateEngine().Process(KeyboardMessage.KeyDown(Key1), InCommand(names: "", cmdActive: EditorState.CmdActiveLisp));

        Assert.Equal("\x03\x03_LINE\n", d.Text);
    }

    [Fact]
    public void Custom_cancel_sequence_is_used()
    {
        var engine = CreateEngine(c => c.CancelSequence = "^[^[");

        Assert.Equal("\x1B\x1B_LINE\n", engine.Process(KeyboardMessage.KeyDown(Key1), InCommand()).Text);
    }

    [Fact]
    public void Focus_outside_drawing_area_passes_key_to_autocad()
    {
        var state = Idle();
        state.DrawingHasFocus = false;

        var d = CreateEngine().Process(KeyboardMessage.KeyDown(Key1), state);

        Assert.Equal(KeyAction.PassThrough, d.Action);
        Assert.False(d.Handled);
    }

    [Fact]
    public void Non_hotkey_and_different_modifiers_pass_through()
    {
        var engine = CreateEngine();

        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown('9'), Idle()).Action);
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1, KeyModifiers.Shift), Idle()).Action); // "!"
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown('3'), Idle()).Action); // only Ctrl+3 is bound
        Assert.Equal("(c:my3)\n", engine.Process(KeyboardMessage.KeyDown('3', KeyModifiers.Ctrl), Idle()).Text);
    }

    [Fact]
    public void PassThrough_policy_types_the_key_while_command_is_active_but_runs_when_idle()
    {
        var engine = CreateEngine();

        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown('2'), InCommand()).Action);
        engine.ResetTyping();
        Assert.Equal("_CIRCLE\n", engine.Process(KeyboardMessage.KeyDown('2'), Idle()).Text);
    }

    [Fact]
    public void Transparent_policy_runs_command_without_cancelling()
    {
        var d = CreateEngine().Process(KeyboardMessage.KeyDown('Q'), InCommand());

        Assert.Equal(KeyAction.Execute, d.Action);
        Assert.Equal("'_ZOOM _E\n", d.Text);
    }

    [Fact]
    public void Prompt_kind_listed_in_passThroughOnPrompts_receives_the_digit()
    {
        var engine = CreateEngine();

        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown('6'), InCommand("OFFSET", prompt: PromptKind.Distance)).Action);
        engine.ResetTyping();
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown('6'), InCommand("OFFSET", prompt: PromptKind.Selection)).Action);
    }

    [Theory]
    [InlineData("MTEXT")]
    [InlineData("TEXT")]
    [InlineData("LINE'DDEDIT")]
    [InlineData("_.MTEXT")]
    public void Text_editing_commands_disable_hotkeys(string names)
    {
        var d = CreateEngine().Process(KeyboardMessage.KeyDown(Key1), InCommand(names));

        Assert.Equal(KeyAction.PassThrough, d.Action);
    }

    [Fact]
    public void Script_disables_hotkeys_unless_configured()
    {
        var script = InCommand(names: "", cmdActive: EditorState.CmdActiveScript);

        Assert.Equal(KeyAction.PassThrough, CreateEngine().Process(KeyboardMessage.KeyDown(Key1), script).Action);
        Assert.Equal(KeyAction.Execute,
            CreateEngine(c => c.IgnoreDuringScript = false).Process(KeyboardMessage.KeyDown(Key1), script).Action);
    }

    [Fact]
    public void Disabled_engine_or_config_passes_everything()
    {
        var engine = CreateEngine();
        engine.Enabled = false;
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);

        var disabledConfig = CreateEngine(c => c.Enabled = false);
        Assert.Equal(KeyAction.PassThrough, disabledConfig.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
    }

    [Fact]
    public void Key_up_of_handled_hotkey_is_swallowed_once()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown(Key1), Idle());

        Assert.Equal(KeyAction.Swallow, engine.Process(KeyboardMessage.KeyUp(Key1), Idle()).Action);
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyUp(Key1), Idle()).Action);
    }

    [Fact]
    public void Auto_repeat_of_handled_hotkey_is_swallowed_and_does_not_rerun()
    {
        var engine = CreateEngine();
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);

        var repeat = engine.Process(KeyboardMessage.KeyDown(Key1, isRepeat: true), InCommand());
        Assert.Equal(KeyAction.Swallow, repeat.Action);
        Assert.Null(repeat.Text);
    }

    [Fact]
    public void Auto_repeat_of_passed_key_is_passed()
    {
        var engine = CreateEngine();
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown('2'), InCommand()).Action);

        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown('2', isRepeat: true), InCommand()).Action);
    }

    [Fact]
    public void After_typing_at_prompt_digits_are_input_until_enter()
    {
        var engine = CreateEngine();
        var state = InCommand("LINE");

        // User types "@1" at "Specify next point:" -> "@" starts typing, "1" must reach AutoCAD.
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(0xDE /* any typing key */), state).Action);
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1), state).Action);
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1), state).Action);

        // Enter completes the input; the next "1" is a hotkey again.
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(VirtualKeys.Return), state).Action);
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), state).Action);
    }

    [Fact]
    public void Typing_a_command_name_at_idle_prompt_keeps_digits_as_text()
    {
        var engine = CreateEngine();

        // "L" then "1": the user is typing something at the command line, so "1" is a character.
        engine.Process(KeyboardMessage.KeyDown(KeyL), Idle());
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
    }

    [Fact]
    public void Typing_state_is_reset_when_the_command_changes()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown(KeyL), Idle());

        // "L" + Space started LINE (detected via CMDNAMES change) -> hotkeys work again.
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), InCommand("LINE")).Action);
    }

    [Fact]
    public void Typing_tracking_can_be_disabled()
    {
        var engine = CreateEngine(c => c.PassThroughWhileTyping = false);
        engine.Process(KeyboardMessage.KeyDown(KeyL), InCommand());

        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), InCommand()).Action);
    }

    [Fact]
    public void Ctrl_shortcuts_do_not_count_as_typing()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown('Z', KeyModifiers.Ctrl), Idle()); // Ctrl+Z (undo)

        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
    }

    [Fact]
    public void Keys_typed_outside_drawing_area_do_not_count_as_typing()
    {
        var engine = CreateEngine();
        var palette = Idle();
        palette.DrawingHasFocus = false;
        engine.Process(KeyboardMessage.KeyDown(KeyL), palette);

        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
    }

    [Fact]
    public void Char_messages_are_never_handled()
    {
        var d = CreateEngine().Process(new KeyboardMessage(WindowMessages.WM_CHAR, Key1), Idle());

        Assert.Equal(KeyAction.PassThrough, d.Action);
    }

    [Fact]
    public void Running_a_hotkey_clears_typing_state()
    {
        var engine = CreateEngine(c => c.Bindings.Add(new HotKeyBinding(KeyChord.Parse("F2"), "_MOVE")));
        engine.Process(KeyboardMessage.KeyDown(KeyL), Idle());
        Assert.True(engine.IsTyping);

        // Function keys are not typing keys: F2 runs and resets the typed state.
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(VirtualKeys.F1 + 1), Idle()).Action);
        Assert.False(engine.IsTyping);
    }

    [Theory]
    [InlineData("modifiers")]
    [InlineData("disabled")]
    [InlineData("reload")]
    public void Held_hotkey_stays_swallowed_after_context_changes(string change)
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown(Key1), Idle());
        if (change == "disabled") engine.Enabled = false;
        if (change == "reload") engine.Config = new HotKeyConfig();
        var modifiers = change == "modifiers" ? KeyModifiers.Shift : KeyModifiers.None;

        Assert.Equal(KeyAction.Swallow, engine.Process(KeyboardMessage.KeyDown(Key1, modifiers, true), Idle()).Action);
        Assert.Equal(KeyAction.Swallow, engine.Process(KeyboardMessage.KeyUp(Key1, modifiers), Idle()).Action);
    }

    [Fact]
    public void Passed_repeat_does_not_become_hotkey_after_modifiers_change()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown('3'), Idle());
        Assert.Equal(KeyAction.PassThrough,
            engine.Process(KeyboardMessage.KeyDown('3', KeyModifiers.Ctrl, true), Idle()).Action);
    }

    [Fact]
    public void Runtime_on_can_enable_a_configuration_that_started_disabled()
    {
        var engine = CreateEngine(c => c.Enabled = false);
        Assert.False(engine.Enabled);
        engine.Enabled = true;
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
    }

    [Fact]
    public void Reload_resets_typing_and_applies_configured_enabled_state()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown(KeyL), Idle());
        engine.Config = engine.Config;
        Assert.False(engine.IsTyping);
        Assert.Equal(KeyAction.Execute, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
    }

    [Fact]
    public void Bound_space_finishes_typed_input_instead_of_running_macro()
    {
        var engine = CreateEngine(c => c.Bindings.Add(new HotKeyBinding(KeyChord.Parse("Space"), "_MOVE")));
        engine.Process(KeyboardMessage.KeyDown(KeyL), Idle());
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(VirtualKeys.Space), Idle()).Action);
        Assert.False(engine.IsTyping);
    }

    [Fact]
    public void Modal_dialog_disables_hotkeys_even_if_message_targets_document()
    {
        Assert.Equal(KeyAction.PassThrough,
            CreateEngine().Process(KeyboardMessage.KeyDown(Key1), InCommand(cmdActive: EditorState.CmdActiveDialog)).Action);
    }

    [Fact]
    public void Release_without_document_snapshot_preserves_typed_input()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown('9'), InCommand());
        engine.Process(KeyboardMessage.KeyUp('9'), new EditorState());
        Assert.True(engine.IsTyping);
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1), InCommand()).Action);
    }

    [Fact]
    public void New_press_after_a_lost_release_replaces_stale_swallowed_state()
    {
        var engine = CreateEngine();
        engine.Process(KeyboardMessage.KeyDown(Key1), Idle());
        engine.Enabled = false;
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyDown(Key1), Idle()).Action);
        Assert.Equal(KeyAction.PassThrough, engine.Process(KeyboardMessage.KeyUp(Key1), Idle()).Action);
    }
}
