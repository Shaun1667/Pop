using System.Collections.Generic;
using System.Linq;
using Pop.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Pop.EditorTools
{
    /// <summary>
    /// 시작 화면(Title.unity)과 Mountain의 HUD · 클리어 화면 · 골인 트리거를 만든다.
    /// - Pop/Dev/Build Title Scene: Title.unity를 새로 만들고 Build Settings 0번에 둔다.
    /// - Pop/Dev/Add Mountain Game UI: 열린 Mountain 씬에 GameUI · EventSystem · 골인 트리거를 더한다(이미 있으면 건너뜀).
    /// - Pop/Dev/Add Pause Menu: 열린 씬의 GameUI(RunManager)에 Esc 일시정지 화면(PausePanel) + PauseMenu를 더한다(이미 있으면 건너뜀). Add Mountain Game UI도 부른다.
    ///   레벨 오브젝트는 옮기거나 지우지 않는다. 골인 트리거만 AcornTree_Goal 아래에 새로 붙인다.
    /// </summary>
    public static class GameUIBuilder
    {
        public const string TitleScenePath = "Assets/_Game/Scenes/Title.unity";
        public const string MountainScenePath = "Assets/_Game/Scenes/Mountain.unity";

        static readonly Color PanelColor = new Color(0.08f, 0.10f, 0.14f, 0.86f);
        static readonly Color ButtonColor = new Color(0.93f, 0.63f, 0.22f, 1f);   // 도토리 주황
        static readonly Color Button2Color = new Color(0.30f, 0.36f, 0.44f, 1f);
        static readonly Color TextColor = new Color(0.97f, 0.95f, 0.90f, 1f);
        static readonly Color AccentColor = new Color(1f, 0.82f, 0.30f, 1f);

        // ------------------------------------------------------------ Title

        [MenuItem("Pop/Dev/Build Title Scene")]
        public static void BuildTitleSceneMenu() => Debug.Log(BuildTitleScene());

        public static string BuildTitleScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.22f, 0.20f);
            camGo.transform.position = new Vector3(0, 1, -10);

            EnsureEventSystem();
            var canvas = MakeCanvas("TitleUI", 0);
            var title = canvas.gameObject.AddComponent<TitleScreen>();
            canvas.gameObject.AddComponent<KoreanUIFont>();

            var root = (RectTransform)canvas.transform;
            // 배경 아래쪽 띠 (산 느낌)
            var band = MakeImage("Ground", root, new Color(0.10f, 0.14f, 0.12f, 1f));
            Stretch(band, new Vector2(0, 0), new Vector2(1, 0.16f));

            var t = MakeText("Title", root, "Pop", 190, AccentColor, FontStyle.Bold);
            Place(t.rectTransform, new Vector2(0.5f, 0.77f), new Vector2(900, 230));
            var sub = MakeText("Subtitle", root, "하늘 다람쥐, 뚫어뻥 하나로 도토리 나무까지", 44, TextColor, FontStyle.Normal);
            Place(sub.rectTransform, new Vector2(0.5f, 0.595f), new Vector2(1400, 70));

            var best = MakeText("BestTime", root, "최고 기록  —:—", 36, AccentColor, FontStyle.Normal);
            Place(best.rectTransform, new Vector2(0.5f, 0.50f), new Vector2(900, 56));

            var start = MakeButton("StartButton", root, "시작", ButtonColor, new Color(0.18f, 0.10f, 0.03f));
            Place((RectTransform)start.transform, new Vector2(0.5f, 0.38f), new Vector2(360, 96));
            var quit = MakeButton("QuitButton", root, "종료", Button2Color, TextColor);
            Place((RectTransform)quit.transform, new Vector2(0.5f, 0.27f), new Vector2(360, 80));

            var hint = MakeText("ControlsHint", root, "A/D 이동   Space 점프   마우스 클릭 뚫어뻥 붙이기 / 떼기   W/S 시점", 26, new Color(0.85f, 0.88f, 0.82f, 0.8f), FontStyle.Normal);
            Place(hint.rectTransform, new Vector2(0.5f, 0.08f), new Vector2(1600, 44));

            title.startButton = start;
            title.quitButton = quit;
            title.bestText = best;

            EditorSceneManager.SaveScene(scene, TitlePath());
            SetBuildOrder();
            return "Title scene saved: " + TitleScenePath + "\n" + BuildOrderText();
        }

        static string TitlePath() => TitleScenePath;

        /// <summary>Build Settings: Title, Mountain, Prototype, 그 밖(원래 순서)</summary>
        public static void SetBuildOrder()
        {
            var list = EditorBuildSettings.scenes.ToList();
            list.RemoveAll(s => s.path == TitleScenePath);
            var order = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(TitleScenePath, true) };
            void Take(string path)
            {
                var s = list.FirstOrDefault(x => x.path == path);
                if (s != null) { order.Add(s); list.Remove(s); }
            }
            Take(MountainScenePath);
            Take("Assets/_Game/Scenes/Prototype.unity");
            order.AddRange(list);
            EditorBuildSettings.scenes = order.ToArray();
        }

        static string BuildOrderText()
        {
            var sb = new System.Text.StringBuilder("Build Settings:\n");
            int i = 0;
            foreach (var s in EditorBuildSettings.scenes) sb.AppendLine($"  {i++}: {s.path} {(s.enabled ? "" : "(off)")}");
            return sb.ToString();
        }

        // ------------------------------------------------------------ Mountain

        [MenuItem("Pop/Dev/Add Mountain Game UI")]
        public static void AddMountainUIMenu() => Debug.Log(AddMountainUI());

        /// <summary>열린 씬에 GameUI(HUD 타이머 · 클리어 화면 · RunManager), EventSystem, 골인 트리거를 더한다. 이미 있으면 건너뛴다.</summary>
        public static string AddMountainUI()
        {
            var log = new System.Text.StringBuilder();
            if (Object.FindFirstObjectByType<EventSystem>() == null) { EnsureEventSystem(); log.AppendLine("EventSystem added"); }

            if (Object.FindFirstObjectByType<RunManager>() == null)
            {
                BuildRunUI();
                log.AppendLine("GameUI added");
            }
            else log.AppendLine("GameUI already there");
            log.AppendLine(EnsurePauseMenuInScene());

            log.AppendLine(EnsureGoalTriggerInScene());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return log.ToString();
        }

        static void BuildRunUI()
        {
            var canvas = MakeCanvas("GameUI", 10);
            var run = canvas.gameObject.AddComponent<RunManager>();
            canvas.gameObject.AddComponent<KoreanUIFont>();
            var root = (RectTransform)canvas.transform;

            // HUD (왼쪽 위)
            var hud = MakeImage("HUD", root, new Color(0.05f, 0.06f, 0.08f, 0.55f));
            hud.rectTransform.anchorMin = hud.rectTransform.anchorMax = new Vector2(0, 1);
            hud.rectTransform.pivot = new Vector2(0, 1);
            hud.rectTransform.anchoredPosition = new Vector2(24, -24);
            hud.rectTransform.sizeDelta = new Vector2(250, 92);
            hud.raycastTarget = false;
            var timer = MakeText("Timer", hud.rectTransform, "00:00.00", 46, TextColor, FontStyle.Bold);
            timer.alignment = TextAnchor.MiddleLeft;
            Stretch(timer.rectTransform, new Vector2(0, 0.38f), new Vector2(1, 1), new Vector2(18, 0), new Vector2(-10, -4));
            var hb = MakeText("Best", hud.rectTransform, "최고 —:—", 22, new Color(1f, 0.85f, 0.45f, 0.95f), FontStyle.Normal);
            hb.alignment = TextAnchor.MiddleLeft;
            Stretch(hb.rectTransform, new Vector2(0, 0), new Vector2(1, 0.40f), new Vector2(18, 6), new Vector2(-10, 0));

            // 클리어 화면
            var dim = MakeImage("ClearPanel", root, new Color(0f, 0f, 0f, 0.55f));
            Stretch(dim, Vector2.zero, Vector2.one);
            var box = MakeImage("Box", dim.rectTransform, PanelColor);
            Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(680, 560));
            var br = box.rectTransform;

            var head = MakeText("Title", br, "클리어!", 96, AccentColor, FontStyle.Bold);
            PlaceIn(head.rectTransform, 0.84f, new Vector2(640, 130));
            var nr = MakeText("NewRecord", br, "새 기록!", 34, new Color(1f, 0.45f, 0.35f), FontStyle.Bold);
            PlaceIn(nr.rectTransform, 0.69f, new Vector2(400, 50));
            var ct = MakeText("RunTime", br, "이번 기록  00:00.00", 44, TextColor, FontStyle.Normal);
            PlaceIn(ct.rectTransform, 0.57f, new Vector2(640, 64));
            var cb = MakeText("BestTime", br, "최고 기록  —:—", 34, new Color(1f, 0.85f, 0.45f), FontStyle.Normal);
            PlaceIn(cb.rectTransform, 0.45f, new Vector2(640, 54));

            var retry = MakeButton("RetryButton", br, "다시 하기", ButtonColor, new Color(0.18f, 0.10f, 0.03f));
            PlaceIn((RectTransform)retry.transform, 0.27f, new Vector2(340, 84));
            var toTitle = MakeButton("TitleButton", br, "타이틀로", Button2Color, TextColor);
            PlaceIn((RectTransform)toTitle.transform, 0.11f, new Vector2(340, 72));

            run.timerText = timer;
            run.hudBestText = hb;
            run.clearPanel = dim.gameObject;
            run.clearTimeText = ct;
            run.clearBestText = cb;
            run.newRecordLabel = nr.gameObject;
            run.retryButton = retry;
            run.titleButton = toTitle;
            dim.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ 일시정지 (Esc)

        [MenuItem("Pop/Dev/Add Pause Menu")]
        public static void AddPauseMenuMenu()
        {
            Debug.Log(EnsurePauseMenuInScene());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// <summary>열린 씬의 RunManager(GameUI)에 PausePanel + PauseMenu를 더한다. 이미 PauseMenu가 있으면 건너뛴다. GameUI 밖은 안 건드린다.</summary>
        public static string EnsurePauseMenuInScene()
        {
            var run = Object.FindFirstObjectByType<RunManager>(FindObjectsInactive.Include);
            if (run == null) return "PauseMenu: no RunManager (GameUI) in this scene";
            var existing = run.GetComponent<PauseMenu>();
            if (existing != null) return "PauseMenu already there";
            BuildPauseUI(run);
            return "PauseMenu added to " + run.gameObject.name;
        }

        static void BuildPauseUI(RunManager run)
        {
            var root = (RectTransform)run.transform;
            var pause = Undo.AddComponent<PauseMenu>(run.gameObject);

            // 어두운 배경 전체 (클리어 화면과 같은 방식). 맨 위(마지막 자식)로 둔다
            var dim = MakeImage("PausePanel", root, new Color(0f, 0f, 0f, 0.6f));
            Undo.RegisterCreatedObjectUndo(dim.gameObject, "Add PausePanel");
            Stretch(dim, Vector2.zero, Vector2.one);
            dim.raycastTarget = true; // 뒤를 누르지 못하게
            dim.transform.SetAsLastSibling();
            var box = MakeImage("Box", dim.rectTransform, PanelColor);
            Place(box.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(600, 440));
            var br = box.rectTransform;

            var head = MakeText("Title", br, "일시정지", 84, AccentColor, FontStyle.Bold);
            PlaceIn(head.rectTransform, 0.78f, new Vector2(560, 120));

            var resume = MakeButton("ResumeButton", br, "돌아가기", ButtonColor, new Color(0.18f, 0.10f, 0.03f));
            PlaceIn((RectTransform)resume.transform, 0.45f, new Vector2(340, 84));
            var quit = MakeButton("QuitButton", br, "게임 종료", Button2Color, TextColor);
            PlaceIn((RectTransform)quit.transform, 0.20f, new Vector2(340, 72));

            var hint = MakeText("Hint", br, "Esc 계속하기", 24, new Color(0.85f, 0.88f, 0.82f, 0.7f), FontStyle.Normal);
            PlaceIn(hint.rectTransform, 0.05f, new Vector2(400, 34));

            pause.pausePanel = dim.gameObject;
            pause.resumeButton = resume;
            pause.quitButton = quit;
            dim.gameObject.SetActive(false);
            EditorUtility.SetDirty(pause);
        }

        // ------------------------------------------------------------ 골인 트리거

        public const string GoalTriggerName = "GoalTrigger";
        public const float GoalHeight = 3.5f;      // 정상 발판 윗면 위로 이만큼 (점프 꼭대기 몸 윗면 ≈ 2.6 m)
        public const float GoalLeftOfTree = 7f;    // 나무 중심에서 왼쪽(오르는 쪽)으로 이만큼부터

        static string EnsureGoalTriggerInScene()
        {
            var tree = GameObject.Find("AcornTree_Goal");
            var plateau = GameObject.Find("Summit_Plateau");
            if (tree == null || plateau == null) return "GoalTrigger: AcornTree_Goal / Summit_Plateau not found";
            var existing = tree.transform.Find(GoalTriggerName);
            if (existing != null) return "GoalTrigger already there: " + DescribeBounds(existing.GetComponent<Collider>().bounds);
            var col = plateau.GetComponent<Collider>();
            var go = CreateGoalTrigger(tree.transform, col.bounds, register: true);
            return "GoalTrigger added: " + DescribeBounds(go.GetComponent<Collider>().bounds);
        }

        /// <summary>
        /// 도토리 나무(tree) 아래에 골인 트리거를 만든다. 상자: x = 나무 − GoalLeftOfTree ~ 정상 발판 오른쪽 끝,
        /// y = 발판 윗면 ~ +GoalHeight, z = ±2. MountainBuilder.Summit()도 이걸 부른다.
        /// </summary>
        public static GameObject CreateGoalTrigger(Transform tree, Bounds plateau, bool register)
        {
            float top = plateau.max.y;
            float x0 = Mathf.Max(plateau.min.x, tree.position.x - GoalLeftOfTree);
            float x1 = plateau.max.x;
            var go = new GameObject(GoalTriggerName);
            if (register) Undo.RegisterCreatedObjectUndo(go, "Add GoalTrigger");
            go.transform.position = new Vector3((x0 + x1) * 0.5f, top + GoalHeight * 0.5f, 0f);
            go.transform.SetParent(tree, true);
            go.transform.rotation = Quaternion.identity;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            Vector3 ls = go.transform.lossyScale;
            box.size = new Vector3((x1 - x0) / Mathf.Max(1e-4f, ls.x), GoalHeight / Mathf.Max(1e-4f, ls.y), 4f / Mathf.Max(1e-4f, ls.z));
            go.AddComponent<GoalTrigger>();
            return go;
        }

        static string DescribeBounds(Bounds b) => $"min {b.min} max {b.max}";

        // ------------------------------------------------------------ UI 도우미

        static void EnsureEventSystem()
        {
            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "Add EventSystem");
            go.AddComponent<EventSystem>();
            var m = go.AddComponent<InputSystemUIInputModule>();
            m.AssignDefaultActions();
        }

        static Canvas MakeCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Add " + name);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        static Font DefaultFont() => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        static Sprite RoundSprite() => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        static Image MakeImage(string name, RectTransform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = RoundSprite();
            img.type = Image.Type.Sliced;
            img.color = color;
            return img;
        }

        static Text MakeText(string name, RectTransform parent, string text, int size, Color color, FontStyle style)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = DefaultFont();
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var sh = go.AddComponent<Shadow>();
            sh.effectColor = new Color(0, 0, 0, 0.45f);
            sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        static Button MakeButton(string name, RectTransform parent, string label, Color bg, Color fg)
        {
            var img = MakeImage(name, parent, bg);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.selectedColor = Color.white;
            b.colors = cb;
            var t = MakeText("Label", img.rectTransform, label, 42, fg, FontStyle.Bold);
            t.GetComponent<Shadow>().enabled = false;
            Stretch(t.rectTransform, Vector2.zero, Vector2.one);
            return b;
        }

        static void Place(RectTransform r, Vector2 anchor, Vector2 size)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = size;
        }

        static void PlaceIn(RectTransform r, float y01, Vector2 size) => Place(r, new Vector2(0.5f, y01), size);

        static void Stretch(Graphic g, Vector2 min, Vector2 max) => Stretch(g.rectTransform, min, max);

        static void Stretch(RectTransform r, Vector2 min, Vector2 max, Vector2 offMin = default, Vector2 offMax = default)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = offMin;
            r.offsetMax = offMax;
        }
    }
}
