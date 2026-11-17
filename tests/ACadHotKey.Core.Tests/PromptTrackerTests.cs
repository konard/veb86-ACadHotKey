using ACadHotKey.Core;
using ACadHotKey.Plugin;
using Autodesk.AutoCAD.ApplicationServices;
using Xunit;

namespace ACadHotKey.Core.Tests;

public class PromptTrackerTests
{
    [Theory]
    [InlineData(PromptKind.Point)]
    [InlineData(PromptKind.Corner)]
    [InlineData(PromptKind.Distance)]
    [InlineData(PromptKind.Angle)]
    [InlineData(PromptKind.Double)]
    [InlineData(PromptKind.Integer)]
    [InlineData(PromptKind.String)]
    [InlineData(PromptKind.Keyword)]
    [InlineData(PromptKind.Selection)]
    [InlineData(PromptKind.Entity)]
    [InlineData(PromptKind.NestedEntity)]
    public void Prompt_completion_by_mouse_resets_typing(PromptKind kind)
    {
        var engine = new HotKeyEngine(new());
        var doc = new Document();
        var tracker = new PromptTracker(_ => engine.ResetTyping());
        tracker.Attach(doc);
        doc.Editor.RaisePrompt(kind);
        Assert.Equal(kind, tracker.Get(doc));
        engine.Process(KeyboardMessage.KeyDown('9'), new() { DrawingHasFocus = true });
        Assert.True(engine.IsTyping);
        doc.Editor.RaisePrompt(kind, completed: true);
        Assert.False(engine.IsTyping);
        Assert.Equal(PromptKind.None, tracker.Get(doc));
    }

    [Theory]
    [InlineData("ended")]
    [InlineData("cancelled")]
    [InlineData("failed")]
    public void Transparent_command_restores_outer_prompt(string outcome)
    {
        var doc = new Document();
        var tracker = new PromptTracker(_ => { });
        tracker.Attach(doc);
        doc.Start("LINE");
        doc.Editor.RaisePrompt(PromptKind.Point);
        doc.Start("ZOOM");
        doc.Editor.RaisePrompt(PromptKind.Keyword);
        doc.Editor.RaisePrompt(PromptKind.Keyword, completed: true);
        doc.Finish("ZOOM", outcome);
        Assert.Equal(PromptKind.Point, tracker.Get(doc));
        doc.Finish("LINE", "ended");
        Assert.Equal(PromptKind.None, tracker.Get(doc));
    }

    [Fact]
    public void Attach_is_idempotent_and_forget_detaches_all_callbacks()
    {
        var doc = new Document();
        int boundaries = 0;
        var tracker = new PromptTracker(_ => boundaries++);
        tracker.Attach(doc);
        tracker.Attach(doc);
        doc.Editor.RaisePrompt(PromptKind.Integer);
        Assert.Equal(1, boundaries);
        tracker.Forget(doc);
        tracker.Forget(doc);
        Assert.Equal(0, doc.SubscriberCount);
        Assert.Equal(0, doc.Editor.SubscriberCount);
        doc.Start("LINE");
        doc.Editor.RaisePrompt(PromptKind.Point);
        Assert.Equal(1, boundaries);
        Assert.Equal(PromptKind.None, tracker.Get(doc));
    }

    [Fact]
    public void Documents_have_independent_prompts_and_typing_boundaries()
    {
        var active = new Document();
        var background = new Document();
        int boundaries = 0;
        var tracker = new PromptTracker(doc => { if (doc == active) boundaries++; });
        tracker.Attach(active);
        tracker.Attach(background);
        active.Editor.RaisePrompt(PromptKind.Point);
        background.Editor.RaisePrompt(PromptKind.Integer);
        Assert.Equal(PromptKind.Point, tracker.Get(active));
        Assert.Equal(PromptKind.Integer, tracker.Get(background));
        Assert.Equal(1, boundaries);
    }
}
