// Small event-only doubles for the real PromptTracker source. They do not simulate native
// keyboard dispatch or prove AutoCAD's built-in command event delivery (manual test M9).
using System.Reflection;
using ACadHotKey.Core;

namespace Autodesk.AutoCAD.ApplicationServices;

internal sealed class CommandEventArgs(string name) : EventArgs
{
    public string GlobalCommandName { get; } = name;
}

internal sealed class Document
{
    public string Name => "test.dwg";
    public TestEditor Editor { get; } = new();
    public event EventHandler<CommandEventArgs>? CommandWillStart;
    public event EventHandler<CommandEventArgs>? CommandEnded;
    public event EventHandler<CommandEventArgs>? CommandCancelled;
    public event EventHandler<CommandEventArgs>? CommandFailed;
    public int SubscriberCount => EventSubscribers.Count(this);
    public void Start(string command) => CommandWillStart?.Invoke(this, new(command));
    public void Finish(string command, string outcome)
    {
        var handler = outcome switch { "cancelled" => CommandCancelled, "failed" => CommandFailed, _ => CommandEnded };
        handler?.Invoke(this, new(command));
    }
}

internal sealed class TestEditor
{
    public event EventHandler? PromptingForPoint;
    public event EventHandler? PromptingForCorner;
    public event EventHandler? PromptingForDistance;
    public event EventHandler? PromptingForAngle;
    public event EventHandler? PromptingForDouble;
    public event EventHandler? PromptingForInteger;
    public event EventHandler? PromptingForString;
    public event EventHandler? PromptingForKeyword;
    public event EventHandler? PromptingForSelection;
    public event EventHandler? PromptingForEntity;
    public event EventHandler? PromptingForNestedEntity;
    public event EventHandler? PromptedForPoint;
    public event EventHandler? PromptedForCorner;
    public event EventHandler? PromptedForDistance;
    public event EventHandler? PromptedForAngle;
    public event EventHandler? PromptedForDouble;
    public event EventHandler? PromptedForInteger;
    public event EventHandler? PromptedForString;
    public event EventHandler? PromptedForKeyword;
    public event EventHandler? PromptedForSelection;
    public event EventHandler? PromptedForEntity;
    public event EventHandler? PromptedForNestedEntity;
    public int SubscriberCount => EventSubscribers.Count(this);

    public void RaisePrompt(PromptKind kind, bool completed = false)
    {
        var handler = (kind, completed) switch
        {
            (PromptKind.Point, false) => PromptingForPoint,
            (PromptKind.Corner, false) => PromptingForCorner,
            (PromptKind.Distance, false) => PromptingForDistance,
            (PromptKind.Angle, false) => PromptingForAngle,
            (PromptKind.Double, false) => PromptingForDouble,
            (PromptKind.Integer, false) => PromptingForInteger,
            (PromptKind.String, false) => PromptingForString,
            (PromptKind.Keyword, false) => PromptingForKeyword,
            (PromptKind.Selection, false) => PromptingForSelection,
            (PromptKind.Entity, false) => PromptingForEntity,
            (PromptKind.NestedEntity, false) => PromptingForNestedEntity,
            (PromptKind.Point, true) => PromptedForPoint,
            (PromptKind.Corner, true) => PromptedForCorner,
            (PromptKind.Distance, true) => PromptedForDistance,
            (PromptKind.Angle, true) => PromptedForAngle,
            (PromptKind.Double, true) => PromptedForDouble,
            (PromptKind.Integer, true) => PromptedForInteger,
            (PromptKind.String, true) => PromptedForString,
            (PromptKind.Keyword, true) => PromptedForKeyword,
            (PromptKind.Selection, true) => PromptedForSelection,
            (PromptKind.Entity, true) => PromptedForEntity,
            (PromptKind.NestedEntity, true) => PromptedForNestedEntity,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        handler?.Invoke(this, EventArgs.Empty);
    }
}

internal static class EventSubscribers
{
    public static int Count(object instance) => instance.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .Select(field => field.GetValue(instance)).OfType<Delegate>().Sum(d => d.GetInvocationList().Length);
}
