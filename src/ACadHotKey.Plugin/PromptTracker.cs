using System.Collections.Generic;
using ACadHotKey.Core;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;

namespace ACadHotKey.Plugin
{
    /// <summary>
    /// Remembers which kind of input each document is currently prompting for, using the
    /// Editor.PromptingForXxx / PromptedForXxx events. Whether these events are raised for the
    /// prompts of built-in commands (and not only for .NET/ObjectARX prompts) must be verified
    /// in AutoCAD with ACHK_DIAG (docs/technical-specification.md, open question Q3).
    /// </summary>
    internal sealed class PromptTracker
    {
        private readonly Dictionary<Document, PromptKind> _prompts = new Dictionary<Document, PromptKind>();

        internal PromptKind Get(Document doc) => _prompts.TryGetValue(doc, out var kind) ? kind : PromptKind.None;

        internal void Reset(Document doc) => Set(doc, PromptKind.None);

        internal void Forget(Document doc) => _prompts.Remove(doc);

        internal void Attach(Document doc)
        {
            var ed = doc.Editor;
            ed.PromptingForPoint += (s, e) => Set(doc, PromptKind.Point);
            ed.PromptingForCorner += (s, e) => Set(doc, PromptKind.Corner);
            ed.PromptingForDistance += (s, e) => Set(doc, PromptKind.Distance);
            ed.PromptingForAngle += (s, e) => Set(doc, PromptKind.Angle);
            ed.PromptingForDouble += (s, e) => Set(doc, PromptKind.Double);
            ed.PromptingForInteger += (s, e) => Set(doc, PromptKind.Integer);
            ed.PromptingForString += (s, e) => Set(doc, PromptKind.String);
            ed.PromptingForKeyword += (s, e) => Set(doc, PromptKind.Keyword);
            ed.PromptingForSelection += (s, e) => Set(doc, PromptKind.Selection);
            ed.PromptingForEntity += (s, e) => Set(doc, PromptKind.Entity);
            ed.PromptingForNestedEntity += (s, e) => Set(doc, PromptKind.NestedEntity);

            ed.PromptedForPoint += (s, e) => Reset(doc);
            ed.PromptedForCorner += (s, e) => Reset(doc);
            ed.PromptedForDistance += (s, e) => Reset(doc);
            ed.PromptedForAngle += (s, e) => Reset(doc);
            ed.PromptedForDouble += (s, e) => Reset(doc);
            ed.PromptedForInteger += (s, e) => Reset(doc);
            ed.PromptedForString += (s, e) => Reset(doc);
            ed.PromptedForKeyword += (s, e) => Reset(doc);
            ed.PromptedForSelection += (s, e) => Reset(doc);
            ed.PromptedForEntity += (s, e) => Reset(doc);
            ed.PromptedForNestedEntity += (s, e) => Reset(doc);
        }

        private void Set(Document doc, PromptKind kind)
        {
            _prompts[doc] = kind;
            TraceLog.Write("prompt " + kind + " in " + doc.Name);
        }
    }
}
