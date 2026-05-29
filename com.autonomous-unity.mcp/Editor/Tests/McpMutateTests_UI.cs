using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace AutonomousMcp.SelfTest
{
    public sealed class McpMutateTests_UI : McpTestHarness
    {
        [Test]
        public void CreateCanvas_makes_a_Canvas()
        {
            AssertOk(Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" }));
            Assert.IsNotNull(GameObject.Find("T_Canvas")?.GetComponent<Canvas>());
        }

        [Test]
        public void CreatePanel_makes_an_Image()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_panel", parent = "T_Canvas", name = "T_Panel" }));
            Assert.IsNotNull(GameObject.Find("T_Panel")?.GetComponent<Image>());
        }

        [Test]
        public void CreateButton_makes_a_Button()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_button", parent = "T_Canvas", name = "T_Btn", label = "Hi" }));
            Assert.IsNotNull(GameObject.Find("T_Btn")?.GetComponent<Button>());
        }

        [Test]
        public void CreateText_makes_a_Text()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_text", parent = "T_Canvas", name = "T_Txt", text = "Hello" }));
            Assert.IsNotNull(GameObject.Find("T_Txt")?.GetComponent<Text>());
        }

        [Test]
        public void CreateImage_makes_an_Image()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            AssertOk(Invoke("unity_ui", new { action = "create_image", parent = "T_Canvas", name = "T_Img" }));
            Assert.IsNotNull(GameObject.Find("T_Img")?.GetComponent<Image>());
        }

        [Test]
        public void SetAnchor_updates_anchorMin()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            Invoke("unity_ui", new { action = "create_image", parent = "T_Canvas", name = "T_Img" });
            AssertOk(Invoke("unity_ui", new { action = "set_anchor", name = "T_Img", min_x = 0.25f, min_y = 0.25f, max_x = 0.75f, max_y = 0.75f }));
            var rt = GameObject.Find("T_Img").GetComponent<RectTransform>();
            Assert.AreEqual(0.25f, rt.anchorMin.x, 0.001f);
            Assert.AreEqual(0.75f, rt.anchorMax.y, 0.001f);
        }

        [Test]
        public void SetRect_executes()
        {
            Invoke("unity_ui", new { action = "create_canvas", name = "T_Canvas" });
            Invoke("unity_ui", new { action = "create_image", parent = "T_Canvas", name = "T_Img" });
            AssertOk(Invoke("unity_ui", new { action = "set_rect", name = "T_Img" }));
        }
    }
}
