using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WeatherHouse.Life
{
    public static class LifeGestureBook
    {
        public static void Build(RectTransform book, LifeSession life, LifeGestureInput input, Font font, string selected, UnityAction<string> select, UnityAction<string> showWord)
        {
            var definition = GestureCatalog.Find(selected) ?? GestureCatalog.All[0];
            LifeUi.Text(book, "Gesture book heading", "天气手势", new Vector2(-290, 300), new Vector2(430, 50), font, 32, LifeUi.Ink);
            LifeUi.Text(book, "Gesture reward source", "跟着笔尖的方向，一笔画完。", new Vector2(-290, 239), new Vector2(430, 65), font, 21, LifeUi.Ink);
            for (int i = 0; i < GestureCatalog.All.Length; i++)
            {
                var gesture = GestureCatalog.All[i]; bool unlocked = life.IsGestureUnlocked(gesture.id), learned = life.IsGestureLearned(gesture.id);
                string title = unlocked ? gesture.title + "    " + (learned ? "已学会" : "练一练") : "寻找线索";
                var button = LifeUi.Button(book, "Gesture " + gesture.id, title,
                    new Vector2(-290, 150 - i * 64), new Vector2(430, 52), font, () => select(gesture.id));
                button.GetComponent<Image>().color = selected == gesture.id ? new Color(.72f, .81f, .70f) : new Color(.87f, .88f, .76f);
            }
            bool available = life.IsGestureUnlocked(definition.id);
            LifeUi.Text(book, "Selected gesture", available ? definition.title : "待发现", new Vector2(290, 300), new Vector2(430, 50), font, 30, LifeUi.Ink);
            if (!available)
            {
                LifeUi.Text(book, "Locked gesture guidance", "在动物的话语里找找线索。", new Vector2(290, 80), new Vector2(410, 180), font, 24, LifeUi.Ink);
                LifeUi.Button(book, "Find reward word", "看看我的记录", new Vector2(290, -90), new Vector2(270, 48), font, () => showWord(definition.id));
                return;
            }
            var demoPaper = LifeUi.Panel(book, "Demonstration paper", new Vector2(290, 185), new Vector2(430, 168), new Color(.97f, .96f, .88f));
            var demoRoot = LifeUi.Rect(demoPaper.transform, "Gesture demonstration", Vector2.zero, new Vector2(180, 150));
            var guide = LifeUi.Rect(demoRoot, "Complete gesture guide", Vector2.zero, new Vector2(180, 150)).gameObject.AddComponent<LifeStrokeGraphic>();
            guide.material = null; guide.color = new Color(.29f, .40f, .30f, .3f); guide.width = 1.8f;
            var guidePoints = new Vector2[definition.points.Length];
            for (int i = 0; i < guidePoints.Length; i++) guidePoints[i] = definition.points[i] * 65f;
            guide.SetPath(guidePoints);
            var demoInk = LifeUi.Rect(demoRoot, "Animated example", Vector2.zero, new Vector2(180, 150)).gameObject.AddComponent<LifeStrokeGraphic>();
            demoInk.color = new Color(.3f, .52f, .44f); demoInk.width = 4f;
            var marker = LifeUi.Panel(demoRoot, "Moving pen tip", Vector2.zero, new Vector2(11, 11), new Color(.78f, .48f, .16f));
            var demo = demoRoot.gameObject.AddComponent<LifeGestureDemo>(); demo.definition = definition; demo.ink = demoInk; demo.marker = marker.rectTransform;
            LifeUi.Text(book, "Stroke direction", definition.instruction, new Vector2(290, 52), new Vector2(420, 78), font, 19, LifeUi.Ink);
            var panel = LifeUi.Panel(book, "Practice pad", new Vector2(290, -91), new Vector2(430, 190), new Color(.95f, .95f, .87f), true);
            LifeUi.Text(panel.transform, "Practice instruction", "按住鼠标左键，在这里照着描画", new Vector2(0, 73), new Vector2(410, 28), font, 17, new Color(.48f, .52f, .44f), TextAnchor.MiddleCenter);
            var stroke = LifeUi.Rect(panel.transform, "Practice ink", Vector2.zero, panel.rectTransform.sizeDelta).gameObject.AddComponent<LifeStrokeGraphic>(); stroke.width = 4f;
            var result = LifeUi.Text(book, "Practice feedback", life.IsGestureLearned(definition.id) ? "学会了。合上手账，按 G 试试。" : "按住左键，跟着笔尖画一次。", new Vector2(290, -224), new Vector2(430, 64), font, 20, LifeUi.Ink);
            var pad = panel.gameObject.AddComponent<LifeGesturePad>(); pad.input = input; pad.gestureId = definition.id; pad.feedback = result; pad.ink = stroke;
            LifeUi.Button(book, "Retry practice", "清空重练", new Vector2(170, -295), new Vector2(180, 45), font, pad.ResetStroke);
            LifeUi.Button(book, "Reward evidence", "看看我的记录", new Vector2(395, -295), new Vector2(230, 45), font,
                () => showWord(definition.id == "clear" ? "rain" : definition.id));
        }
    }
}
