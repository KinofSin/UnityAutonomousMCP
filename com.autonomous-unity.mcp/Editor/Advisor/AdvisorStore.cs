using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;

namespace AutonomousMcp.Editor.Advisor
{
    /// <summary>
    /// Single source of truth for the Advisor HUD: the advice feed (AI -> user) and the outbox
    /// queue (user -> AI). Both are bounded ring buffers persisted to SessionState so they survive
    /// the domain reloads recompiles trigger. Pure state + serialization — no UI, no network.
    /// </summary>
    internal static class AdvisorStore
    {
        public const int MaxAdvice = 100;
        public const int MaxOutbox = 50;

        private const string AdviceKey = "AutonomousMcp.Advisor.Advice";
        private const string OutboxKey = "AutonomousMcp.Advisor.Outbox";

        private static readonly List<AdviceItem> _advice = new List<AdviceItem>();
        private static readonly List<OutboxItem> _outbox = new List<OutboxItem>();
        private static bool _loaded;

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            TryLoad(AdviceKey, _advice);
            TryLoad(OutboxKey, _outbox);
        }

        private static void TryLoad<T>(string key, List<T> into)
        {
            into.Clear();
            var json = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var list = JsonConvert.DeserializeObject<List<T>>(json);
                if (list != null) into.AddRange(list);
            }
            catch { /* corrupt/empty — start fresh */ }
        }

        private static void PersistAdvice() => SessionState.SetString(AdviceKey, JsonConvert.SerializeObject(_advice));
        private static void PersistOutbox() => SessionState.SetString(OutboxKey, JsonConvert.SerializeObject(_outbox));

        // ── advice feed (AI -> user) ─────────────────────────────────────────────────

        public static void AddText(string text, string level)
        {
            EnsureLoaded();
            _advice.Add(new AdviceItem
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 8),
                kind = "text",
                level = string.IsNullOrEmpty(level) ? "info" : level,
                text = text ?? string.Empty,
                postedAtUtc = DateTime.UtcNow.ToString("o")
            });
            while (_advice.Count > MaxAdvice) _advice.RemoveAt(0);
            PersistAdvice();
        }

        public static List<AdviceItem> GetAdvice()
        {
            EnsureLoaded();
            return new List<AdviceItem>(_advice);
        }

        // ── outbox (user -> AI) ──────────────────────────────────────────────────────

        public static void Enqueue(string type, string payloadJson)
        {
            EnsureLoaded();
            _outbox.Add(new OutboxItem
            {
                type = type,
                payload = payloadJson ?? string.Empty,
                enqueuedAtUtc = DateTime.UtcNow.ToString("o")
            });
            while (_outbox.Count > MaxOutbox) _outbox.RemoveAt(0);
            PersistOutbox();
        }

        public static int PendingCount()
        {
            EnsureLoaded();
            return _outbox.Count;
        }

        public static List<OutboxItem> DrainOutbox()
        {
            EnsureLoaded();
            var copy = new List<OutboxItem>(_outbox);
            _outbox.Clear();
            PersistOutbox();
            return copy;
        }

        // ── test seams ───────────────────────────────────────────────────────────────

        // Full reset: clears RAM and SessionState (test isolation).
        public static void ResetForTests()
        {
            _advice.Clear();
            _outbox.Clear();
            _loaded = true;
            SessionState.EraseString(AdviceKey);
            SessionState.EraseString(OutboxKey);
        }

        // Drop RAM only, leaving SessionState intact (simulates a domain reload).
        public static void DropInMemoryForTests()
        {
            _advice.Clear();
            _outbox.Clear();
            _loaded = false;
        }
    }
}
