using System;
using System.Collections.Generic;

namespace AutonomousMcp.Editor.Advisor
{
    // One advice entry shown in the HUD feed. kind = "text" or "card".
    [Serializable]
    public sealed class AdviceItem
    {
        public string id;
        public string kind;          // "text" | "card"
        public string level;         // "info" | "success" | "warning"
        public string text;          // kind == "text"
        public string title;         // kind == "card"
        public string body;          // kind == "card"
        public List<CardAction> actions = new List<CardAction>(); // kind == "card"
        public string postedAtUtc;
    }

    [Serializable]
    public sealed class CardAction
    {
        public string id;
        public string label;
    }

    // One queued user->AI item. payload is a free-form JSON string (note text,
    // selection summary, console entries, etc.) interpreted by the AI client.
    [Serializable]
    public sealed class OutboxItem
    {
        public string type;          // note|selection|screenshot|console|card_action|quick_ask
        public string payload;       // JSON string
        public string enqueuedAtUtc;
    }
}
