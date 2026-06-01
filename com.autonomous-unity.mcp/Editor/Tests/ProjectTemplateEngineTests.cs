using NUnit.Framework;
using AutonomousMcp.Editor.Templates;

namespace AutonomousMcp.SelfTest
{
    // Pure unit tests for the deterministic template engine — no Unity scene, no VRChat SDK.
    public sealed class ProjectTemplateEngineTests
    {
        [Test]
        public void ClassifyPlatform_detects_quest_from_name()
        {
            Assert.AreEqual("quest", ProjectTemplateEngine.ClassifyPlatform("LEAF QUEST"));
            Assert.AreEqual("quest", ProjectTemplateEngine.ClassifyPlatform("avatar_Android"));
            Assert.AreEqual("pc", ProjectTemplateEngine.ClassifyPlatform("LEAF"));
            Assert.AreEqual("unknown", ProjectTemplateEngine.ClassifyPlatform(""));
        }

        [Test]
        public void ComputeSteps_marks_done_from_state()
        {
            var s = new AvatarState
            {
                hasDescriptor = true,
                hasViewpoint = false,
                hasExpressionMenu = true,
                hasExpressionParams = false,
                hasFolders = true
            };
            var steps = ProjectTemplateEngine.ComputeSteps(s);
            Assert.AreEqual(4, steps.Count);
            Assert.IsTrue(steps.Find(x => x.id == "descriptor").done);
            Assert.IsFalse(steps.Find(x => x.id == "viewpoint").done);
            Assert.IsFalse(steps.Find(x => x.id == "expressions").done, "needs both menu and params");
            Assert.IsTrue(steps.Find(x => x.id == "folders").done);
        }

        [Test]
        public void ComputeSteps_expressions_done_only_when_both_present()
        {
            var s = new AvatarState { hasExpressionMenu = true, hasExpressionParams = true };
            Assert.IsTrue(ProjectTemplateEngine.ComputeSteps(s).Find(x => x.id == "expressions").done);
        }
    }
}
