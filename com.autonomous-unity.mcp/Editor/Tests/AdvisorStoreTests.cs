using NUnit.Framework;
using AutonomousMcp.Editor.Advisor;

namespace AutonomousMcp.SelfTest
{
    // Pure unit tests for the Advisor store — no bridge, no keys, no UI. Each test resets the
    // store (RAM + SessionState) first for isolation.
    public sealed class AdvisorStoreTests
    {
        [SetUp]
        public void Reset() => AdvisorStore.ResetForTests();

        [Test]
        public void AddAdvice_appends_and_returns_in_order()
        {
            AdvisorStore.AddText("first", "info");
            AdvisorStore.AddText("second", "warning");
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual("first", all[0].text);
            Assert.AreEqual("second", all[1].text);
            Assert.AreEqual("warning", all[1].level);
        }

        [Test]
        public void AddAdvice_caps_at_MaxAdvice_dropping_oldest()
        {
            for (int i = 0; i < AdvisorStore.MaxAdvice + 10; i++)
                AdvisorStore.AddText("a" + i, "info");
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(AdvisorStore.MaxAdvice, all.Count);
            Assert.AreEqual("a10", all[0].text); // first 10 dropped
        }

        [Test]
        public void Outbox_enqueue_then_drain_returns_in_order_and_clears()
        {
            AdvisorStore.Enqueue("note", "{\"text\":\"hi\"}");
            AdvisorStore.Enqueue("console", "{\"entries\":[]}");
            Assert.AreEqual(2, AdvisorStore.PendingCount());

            var drained = AdvisorStore.DrainOutbox();
            Assert.AreEqual(2, drained.Count);
            Assert.AreEqual("note", drained[0].type);
            Assert.AreEqual("console", drained[1].type);
            Assert.AreEqual(0, AdvisorStore.PendingCount(), "drain clears the queue");
            Assert.AreEqual(0, AdvisorStore.DrainOutbox().Count, "second drain is empty");
        }

        [Test]
        public void Outbox_caps_at_MaxOutbox_dropping_oldest()
        {
            for (int i = 0; i < AdvisorStore.MaxOutbox + 5; i++)
                AdvisorStore.Enqueue("note", "{\"n\":" + i + "}");
            Assert.AreEqual(AdvisorStore.MaxOutbox, AdvisorStore.PendingCount());
            var drained = AdvisorStore.DrainOutbox();
            StringAssert.Contains("\"n\":5", drained[0].payload); // first 5 dropped
        }

        [Test]
        public void AddCard_stores_card_with_actions()
        {
            AdvisorStore.AddCard("c1", "No visemes", "explanation",
                new System.Collections.Generic.List<CardAction> { new CardAction { id = "approve", label = "Approve fix" } });
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("card", all[0].kind);
            Assert.AreEqual("c1", all[0].id);
            Assert.AreEqual("No visemes", all[0].title);
            Assert.AreEqual(1, all[0].actions.Count);
            Assert.AreEqual("approve", all[0].actions[0].id);
        }

        [Test]
        public void State_round_trips_through_SessionState()
        {
            AdvisorStore.AddText("persisted advice", "info");
            AdvisorStore.Enqueue("note", "{\"text\":\"persisted note\"}");

            // Simulate a domain reload: drop in-memory state, then reload from SessionState.
            AdvisorStore.DropInMemoryForTests();
            AdvisorStore.EnsureLoaded();

            Assert.AreEqual(1, AdvisorStore.GetAdvice().Count);
            Assert.AreEqual("persisted advice", AdvisorStore.GetAdvice()[0].text);
            Assert.AreEqual(1, AdvisorStore.PendingCount());
            Assert.AreEqual("note", AdvisorStore.DrainOutbox()[0].type);
        }

        [Test]
        public void GetOutbox_inspects_without_draining()
        {
            AdvisorStore.Enqueue("note", "{\"text\":\"a\"}");
            AdvisorStore.Enqueue("quick_ask", "{\"key\":\"whats_next\"}");
            var peek = AdvisorStore.GetOutbox();
            Assert.AreEqual(2, peek.Count);
            Assert.AreEqual(2, AdvisorStore.PendingCount(), "GetOutbox must not drain");
            Assert.AreEqual("note", peek[0].type);
        }

        [Test]
        public void RemoveOutboxAt_removes_one_item()
        {
            AdvisorStore.Enqueue("note", "{\"text\":\"a\"}");
            AdvisorStore.Enqueue("note", "{\"text\":\"b\"}");
            AdvisorStore.Enqueue("note", "{\"text\":\"c\"}");
            Assert.IsTrue(AdvisorStore.RemoveOutboxAt(1));
            Assert.IsFalse(AdvisorStore.RemoveOutboxAt(99));
            var left = AdvisorStore.GetOutbox();
            Assert.AreEqual(2, left.Count);
            StringAssert.Contains("\"text\":\"a\"", left[0].payload);
            StringAssert.Contains("\"text\":\"c\"", left[1].payload);
        }

        [Test]
        public void ClearOutbox_empties_queue()
        {
            AdvisorStore.Enqueue("note", "{}");
            AdvisorStore.Enqueue("console", "{}");
            AdvisorStore.ClearOutbox();
            Assert.AreEqual(0, AdvisorStore.PendingCount());
            Assert.AreEqual(0, AdvisorStore.GetOutbox().Count);
        }

        [Test]
        public void DismissAdvice_removes_by_id()
        {
            AdvisorStore.AddText("keep", "info");
            AdvisorStore.AddCard("drop-me", "Title", "Body", null);
            Assert.IsTrue(AdvisorStore.DismissAdvice("drop-me"));
            Assert.IsFalse(AdvisorStore.DismissAdvice("nope"));
            var all = AdvisorStore.GetAdvice();
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual("keep", all[0].text);
        }

        [Test]
        public void ClearAdvice_empties_feed()
        {
            AdvisorStore.AddText("a", "info");
            AdvisorStore.AddText("b", "warning");
            AdvisorStore.ClearAdvice();
            Assert.AreEqual(0, AdvisorStore.GetAdvice().Count);
        }

        [Test]
        public void New_APIs_survive_domain_reload()
        {
            AdvisorStore.AddText("advice", "info");
            AdvisorStore.Enqueue("note", "{\"text\":\"n\"}");
            var id = AdvisorStore.GetAdvice()[0].id;

            AdvisorStore.DropInMemoryForTests();
            AdvisorStore.EnsureLoaded();

            Assert.AreEqual(1, AdvisorStore.GetOutbox().Count);
            Assert.IsTrue(AdvisorStore.DismissAdvice(id));
            Assert.AreEqual(0, AdvisorStore.GetAdvice().Count);
            Assert.IsTrue(AdvisorStore.RemoveOutboxAt(0));
            Assert.AreEqual(0, AdvisorStore.PendingCount());
        }
    }
}
