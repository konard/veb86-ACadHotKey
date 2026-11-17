using System;
using System.Collections.Generic;
using ACadHotKey.Core;
using Autodesk.AutoCAD.ApplicationServices;

namespace ACadHotKey.Plugin
{
    /// <summary>
    /// Tracks prompt boundaries per document and restores the outer prompt after a transparent command.
    /// Built-in command event delivery must still be checked in each AutoCAD release (Q3 in the specification).
    /// </summary>
    internal sealed class PromptTracker
    {
        private readonly Dictionary<Document, Subscription> _documents = new Dictionary<Document, Subscription>();
        private readonly Action<Document> _onBoundary;

        internal PromptTracker(Action<Document> onBoundary) => _onBoundary = onBoundary;
        internal PromptKind Get(Document doc) => _documents.TryGetValue(doc, out var entry) ? entry.Kind : PromptKind.None;

        internal void Attach(Document doc)
        {
            if (!_documents.ContainsKey(doc)) _documents.Add(doc, new Subscription(doc, _onBoundary));
        }

        internal void Forget(Document doc)
        {
            if (!_documents.TryGetValue(doc, out var entry)) return;
            entry.Dispose();
            _documents.Remove(doc);
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Document _doc;
            private readonly Action<Document> _onBoundary;
            private readonly Stack<PromptKind> _outerPrompts = new Stack<PromptKind>();
            internal PromptKind Kind { get; private set; }

            internal Subscription(Document doc, Action<Document> onBoundary)
            {
                _doc = doc;
                _onBoundary = onBoundary;
                var ed = doc.Editor;
                ed.PromptingForPoint += Point;
                ed.PromptingForCorner += Corner;
                ed.PromptingForDistance += Distance;
                ed.PromptingForAngle += Angle;
                ed.PromptingForDouble += Double;
                ed.PromptingForInteger += Integer;
                ed.PromptingForString += String;
                ed.PromptingForKeyword += Keyword;
                ed.PromptingForSelection += Selection;
                ed.PromptingForEntity += Entity;
                ed.PromptingForNestedEntity += NestedEntity;
                ed.PromptedForPoint += Completed;
                ed.PromptedForCorner += Completed;
                ed.PromptedForDistance += Completed;
                ed.PromptedForAngle += Completed;
                ed.PromptedForDouble += Completed;
                ed.PromptedForInteger += Completed;
                ed.PromptedForString += Completed;
                ed.PromptedForKeyword += Completed;
                ed.PromptedForSelection += Completed;
                ed.PromptedForEntity += Completed;
                ed.PromptedForNestedEntity += Completed;
                doc.CommandWillStart += Starting;
                doc.CommandEnded += Finished;
                doc.CommandCancelled += Finished;
                doc.CommandFailed += Finished;
            }

            private void Set(PromptKind kind)
            {
                Kind = kind;
                _onBoundary(_doc); // Mouse input also ends typing; no Enter key is required.
                if (TraceLog.Enabled) TraceLog.Write("prompt " + kind + " in " + _doc.Name);
            }

            private void Starting(object? sender, CommandEventArgs e)
            {
                _outerPrompts.Push(Kind);
                Set(PromptKind.None);
                if (TraceLog.Enabled) TraceLog.Write("command start " + e.GlobalCommandName);
            }

            private void Finished(object? sender, CommandEventArgs e)
            {
                Set(_outerPrompts.Count == 0 ? PromptKind.None : _outerPrompts.Pop());
                if (TraceLog.Enabled) TraceLog.Write("command finish " + e.GlobalCommandName);
            }

            private void Point(object? sender, EventArgs e) => Set(PromptKind.Point);
            private void Corner(object? sender, EventArgs e) => Set(PromptKind.Corner);
            private void Distance(object? sender, EventArgs e) => Set(PromptKind.Distance);
            private void Angle(object? sender, EventArgs e) => Set(PromptKind.Angle);
            private void Double(object? sender, EventArgs e) => Set(PromptKind.Double);
            private void Integer(object? sender, EventArgs e) => Set(PromptKind.Integer);
            private void String(object? sender, EventArgs e) => Set(PromptKind.String);
            private void Keyword(object? sender, EventArgs e) => Set(PromptKind.Keyword);
            private void Selection(object? sender, EventArgs e) => Set(PromptKind.Selection);
            private void Entity(object? sender, EventArgs e) => Set(PromptKind.Entity);
            private void NestedEntity(object? sender, EventArgs e) => Set(PromptKind.NestedEntity);
            private void Completed(object? sender, EventArgs e) => Set(PromptKind.None);

            public void Dispose()
            {
                var ed = _doc.Editor;
                ed.PromptingForPoint -= Point;
                ed.PromptingForCorner -= Corner;
                ed.PromptingForDistance -= Distance;
                ed.PromptingForAngle -= Angle;
                ed.PromptingForDouble -= Double;
                ed.PromptingForInteger -= Integer;
                ed.PromptingForString -= String;
                ed.PromptingForKeyword -= Keyword;
                ed.PromptingForSelection -= Selection;
                ed.PromptingForEntity -= Entity;
                ed.PromptingForNestedEntity -= NestedEntity;
                ed.PromptedForPoint -= Completed;
                ed.PromptedForCorner -= Completed;
                ed.PromptedForDistance -= Completed;
                ed.PromptedForAngle -= Completed;
                ed.PromptedForDouble -= Completed;
                ed.PromptedForInteger -= Completed;
                ed.PromptedForString -= Completed;
                ed.PromptedForKeyword -= Completed;
                ed.PromptedForSelection -= Completed;
                ed.PromptedForEntity -= Completed;
                ed.PromptedForNestedEntity -= Completed;
                _doc.CommandWillStart -= Starting;
                _doc.CommandEnded -= Finished;
                _doc.CommandCancelled -= Finished;
                _doc.CommandFailed -= Finished;
                _outerPrompts.Clear();
            }
        }
    }
}
