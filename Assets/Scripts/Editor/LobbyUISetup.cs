using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.Editor
{
    public static class LobbyUISetup
    {
        [MenuItem("AbsoluteZero/Setup Lobby UI")]
        public static void SetupLobbyUI()
        {
            if (Application.isPlaying)
            {
                Debug.LogError("[LobbyUISetup] Play 모드에서 실행 불가 — 먼저 Play 모드를 중지하세요");
                return;
            }

            var mainUI = GameObject.Find("MainUI");
            if (mainUI == null)
            {
                Debug.LogError("[LobbyUISetup] MainUI Canvas가 씬에 없습니다");
                return;
            }

            // 기존 런타임 생성된 패널 삭제
            for (int i = mainUI.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(mainUI.transform.GetChild(i).gameObject);

            var root = mainUI.transform;

            BuildMainPanel(root);
            BuildModeSelectPanel(root);
            BuildRoomPanel(root);
            BuildSettingsPanel(root);
            BuildClosetPanel(root);

            var lobbyUIGO = GameObject.Find("LobbyUI");
            if (lobbyUIGO != null)
            {
                var lobbyUI = lobbyUIGO.GetComponent<AbsoluteZero.UI.LobbyUI.AZLobbyUI>();
                if (lobbyUI != null)
                {
                    var so = new SerializedObject(lobbyUI);
                    so.FindProperty("_mainPanel").objectReferenceValue = root.Find("MainPanel")?.gameObject;
                    so.FindProperty("_modeSelectPanel").objectReferenceValue = root.Find("ModeSelectPanel")?.gameObject;
                    so.FindProperty("_roomPanel").objectReferenceValue = root.Find("RoomPanel")?.gameObject;
                    so.FindProperty("_settingsPanel").objectReferenceValue = root.Find("SettingsPanel")?.gameObject;
                    so.FindProperty("_closetPanel").objectReferenceValue = root.Find("ClosetPanel")?.gameObject;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(lobbyUIGO);
                }
            }

            EditorUtility.SetDirty(mainUI);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log("[LobbyUISetup] 로비 UI 셋업 완료 — 씬 저장하세요 (Ctrl+S)");
        }

        static void BuildMainPanel(Transform root)
        {
            var panel = CreateFullPanel(root, "MainPanel", true);

            // Background
            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(panel.transform, false);
            var bgRT = bgGO.AddComponent<RectTransform>();
            Stretch(bgRT);
            var bgImg = bgGO.AddComponent<UnityEngine.UI.Image>();
            var tex = Resources.Load<Texture2D>("로비화면");
            if (tex != null)
                bgImg.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            bgImg.preserveAspect = false;
            bgImg.raycastTarget = false;

            // NicknameBar
            BuildNicknameBar(panel.transform);

            // Arena 버튼 — 배경의 결투장 아이콘 영역에 맞게 투명 버튼
            BuildOverlayButton(panel.transform, "ArenaBtn", new Vector2(65, 100), new Vector2(130, 130));

            // Closet 버튼
            BuildOverlayButton(panel.transform, "ClosetBtn", new Vector2(65, -40), new Vector2(130, 130));

            // Settings 버튼 — 우상단 기어 아이콘 영역
            BuildOverlayButton(panel.transform, "SettingsBtn", new Vector2(-50, -50), new Vector2(60, 60));
            var settingsRT = panel.transform.Find("SettingsBtn").GetComponent<RectTransform>();
            settingsRT.anchorMin = new Vector2(1, 1);
            settingsRT.anchorMax = new Vector2(1, 1);
            settingsRT.pivot = new Vector2(1, 1);
            settingsRT.anchoredPosition = new Vector2(-20, -20);
            var settingsImage = settingsRT.GetComponent<UnityEngine.UI.Image>();
            settingsImage.sprite = Resources.Load<Sprite>("lobby_settings");
            settingsImage.color = Color.white;
            settingsImage.preserveAspect = true;

            // Status
            var status = CreateTMP(panel.transform, "StatusText",
                new Vector2(45, 10), new Vector2(400, 30), "준비 완료", 18,
                new Color(0.61f, 0.56f, 0.50f));
            status.alignment = TextAlignmentOptions.BottomLeft;
            var srt = status.GetComponent<RectTransform>();
            srt.anchorMin = new Vector2(0, 0);
            srt.anchorMax = new Vector2(0, 0);
            srt.pivot = new Vector2(0, 0);
        }

        static void BuildOverlayButton(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(1, 1, 1, 0); // 투명 — 배경의 아이콘 위에 올려둠
            img.raycastTarget = true;

            go.AddComponent<UnityEngine.UI.Button>();
        }

        static void BuildNicknameBar(Transform parent)
        {
            var sprites = Resources.LoadAll<Sprite>("로비화면");
            Sprite nickSprite = null;
            foreach (var s in sprites)
                if (s.name == "lobby_nickname") nickSprite = s;

            var go = new GameObject("NicknameBar");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(30, -25);
            rt.sizeDelta = new Vector2(340, 85);

            var img = go.AddComponent<UnityEngine.UI.Image>();
            if (nickSprite != null) img.sprite = nickSprite;
            img.preserveAspect = true;

            // TextArea
            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(go.transform, false);
            var taRT = textArea.AddComponent<RectTransform>();
            Stretch(taRT);
            taRT.offsetMin = new Vector2(15, 0);
            taRT.offsetMax = new Vector2(-55, 0);
            textArea.AddComponent<RectMask2D>();

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(textArea.transform, false);
            var phRT = phGO.AddComponent<RectTransform>();
            Stretch(phRT);
            var phText = phGO.AddComponent<TextMeshProUGUI>();
            phText.text = "닉네임 입력";
            phText.fontSize = 22;
            phText.fontStyle = FontStyles.Italic;
            phText.color = new Color(0.61f, 0.56f, 0.50f, 0.7f);
            phText.alignment = TextAlignmentOptions.MidlineLeft;

            var inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(textArea.transform, false);
            var itRT = inputTextGO.AddComponent<RectTransform>();
            Stretch(itRT);
            var inputTMP = inputTextGO.AddComponent<TextMeshProUGUI>();
            inputTMP.fontSize = 22;
            inputTMP.color = new Color(0.17f, 0.09f, 0.06f);
            inputTMP.alignment = TextAlignmentOptions.MidlineLeft;

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = taRT;
            input.textComponent = inputTMP;
            input.placeholder = phText;
            input.characterLimit = 8;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.pointSize = 22;
        }

        static void BuildModeSelectPanel(Transform root)
        {
            var panel = CreateFullPanel(root, "ModeSelectPanel", false);
            BuildDim(panel.transform, "Dim");

            var bg = CreateCenterPanel(panel.transform, "PanelBG", new Vector2(500, 350),
                new Color(0.96f, 0.93f, 0.88f, 0.96f));

            var title = CreateTMP(bg, "Title", new Vector2(0, 130), new Vector2(400, 50),
                "모드 선택", 36, new Color(0.17f, 0.09f, 0.06f));
            title.fontStyle = FontStyles.Bold;

            CreateTextButton(bg, "OneVsOneBtn", new Vector2(0, 30), new Vector2(350, 60),
                "1 vs 1", new Color(0.75f, 0.22f, 0.17f), 26);

            CreateTextButton(bg, "Multi4Btn", new Vector2(0, -50), new Vector2(350, 60),
                "다인전 (4인)", new Color(0.75f, 0.22f, 0.17f), 26);

            CreateTextButton(bg, "BackLink", new Vector2(0, -140), new Vector2(200, 40),
                "← 뒤로", new Color(0.55f, 0.45f, 0.33f), 20);
        }

        static void BuildRoomPanel(Transform root)
        {
            var panel = CreateFullPanel(root, "RoomPanel", false);
            BuildDim(panel.transform, "Dim");

            var bg = CreateCenterPanel(panel.transform, "PanelBG", new Vector2(580, 580),
                new Color(0.96f, 0.93f, 0.88f, 0.96f));

            CreateTMP(bg, "ModeLabel", new Vector2(0, 250), new Vector2(400, 30),
                "1 vs 1 대결", 20, new Color(0.55f, 0.45f, 0.33f));

            var title = CreateTMP(bg, "Title", new Vector2(0, 225), new Vector2(400, 40),
                "대기실", 32, new Color(0.17f, 0.09f, 0.06f));
            title.fontStyle = FontStyles.Bold;

            // LobbyCode
            var codeGO = new GameObject("LobbyCode");
            codeGO.transform.SetParent(bg, false);
            var codeRT = codeGO.AddComponent<RectTransform>();
            codeRT.anchoredPosition = new Vector2(0, 185);
            codeRT.sizeDelta = new Vector2(300, 40);
            codeGO.AddComponent<UnityEngine.UI.Image>().color = new Color(0.15f, 0.12f, 0.08f, 0.9f);
            codeGO.AddComponent<UnityEngine.UI.Button>();
            var codeTmp = CreateTMP(codeGO.transform, "CodeText", Vector2.zero, Vector2.zero,
                "------", 24, new Color(0.83f, 0.65f, 0.29f));
            codeTmp.characterSpacing = 15;
            Stretch(codeTmp.GetComponent<RectTransform>());

            // JoinInput
            BuildInputField(bg, "JoinInput", new Vector2(-60, 135), new Vector2(220, 45), "코드 입력");

            // Buttons row
            CreateTextButton(bg, "CreateBtn", new Vector2(0, 135), new Vector2(120, 45),
                "생성", new Color(0.24f, 0.48f, 0.50f), 20);
            CreateTextButton(bg, "JoinBtn", new Vector2(130, 135), new Vector2(100, 45),
                "참가", new Color(0.55f, 0.45f, 0.33f), 20);

            // PlayerList
            var plGO = new GameObject("PlayerList");
            plGO.transform.SetParent(bg, false);
            var plRT = plGO.AddComponent<RectTransform>();
            plRT.anchoredPosition = new Vector2(0, 20);
            plRT.sizeDelta = new Vector2(500, 180);
            var vlg = plGO.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childAlignment = TextAnchor.UpperCenter;

            // LogScroll
            var logGO = new GameObject("LogScroll");
            logGO.transform.SetParent(bg, false);
            var logRT = logGO.AddComponent<RectTransform>();
            logRT.anchoredPosition = new Vector2(0, -120);
            logRT.sizeDelta = new Vector2(500, 80);
            logGO.AddComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.10f, 0.08f, 0.5f);
            var logTMP = CreateTMP(logGO.transform, "LogText", Vector2.zero, Vector2.zero,
                "", 14, new Color(0.78f, 0.75f, 0.71f));
            logTMP.alignment = TextAlignmentOptions.TopLeft;
            Stretch(logTMP.GetComponent<RectTransform>());
            logTMP.GetComponent<RectTransform>().offsetMin = new Vector2(8, 4);
            logTMP.GetComponent<RectTransform>().offsetMax = new Vector2(-8, -4);

            // StatusText
            CreateTMP(bg, "StatusText", new Vector2(0, -180), new Vector2(480, 30),
                "", 16, new Color(0.61f, 0.56f, 0.50f));

            // Bottom buttons
            CreateTextButton(bg, "StartBtn", new Vector2(-80, -220), new Vector2(140, 45),
                "시작", new Color(0.24f, 0.48f, 0.50f), 22);
            CreateTextButton(bg, "LeaveBtn", new Vector2(80, -220), new Vector2(140, 45),
                "나가기", new Color(0.75f, 0.22f, 0.17f), 22);
            CreateTextButton(bg, "BackLink", new Vector2(0, -260), new Vector2(200, 35),
                "← 뒤로", new Color(0.55f, 0.45f, 0.33f), 18);
        }

        static void BuildSettingsPanel(Transform root)
        {
            var panel = CreateFullPanel(root, "SettingsPanel", false);
            BuildDim(panel.transform, "Dim");

            var bg = CreateCenterPanel(panel.transform, "PanelBG", new Vector2(500, 400),
                new Color(0.96f, 0.93f, 0.88f, 0.96f));

            var title = CreateTMP(bg, "Title", new Vector2(0, 160), new Vector2(400, 50),
                "설정", 36, new Color(0.17f, 0.09f, 0.06f));
            title.fontStyle = FontStyles.Bold;

            BuildVolumeSlider(bg, "BGMSlider", new Vector2(0, 60), "배경음악");
            BuildVolumeSlider(bg, "SFXSlider", new Vector2(0, -20), "효과음");

            CreateTextButton(bg, "CloseBtn", new Vector2(0, -140), new Vector2(200, 50),
                "닫기", new Color(0.55f, 0.45f, 0.33f), 22);
        }

        static void BuildClosetPanel(Transform root)
        {
            var panel = CreateFullPanel(root, "ClosetPanel", false);
            BuildDim(panel.transform, "Dim");

            var bg = CreateCenterPanel(panel.transform, "PanelBG", new Vector2(600, 520),
                new Color(0.96f, 0.93f, 0.88f, 0.96f));

            var title = CreateTMP(bg, "Title", new Vector2(0, 220), new Vector2(500, 50),
                "옷장", 36, new Color(0.17f, 0.09f, 0.06f));
            title.fontStyle = FontStyles.Bold;

            // Tabs
            var tabGO = new GameObject("Tabs");
            tabGO.transform.SetParent(bg, false);
            var tabRT = tabGO.AddComponent<RectTransform>();
            tabRT.anchoredPosition = new Vector2(0, 155);
            tabRT.sizeDelta = new Vector2(520, 45);
            var hlg = tabGO.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            string[] tabLabels = { "머리", "상의", "등", "하의", "꼬리" };
            string[] tabNames = { "Tab_Head", "Tab_Top", "Tab_Back", "Tab_Bottom", "Tab_Tail" };
            for (int i = 0; i < 5; i++)
            {
                var tabBtn = CreateTextButton(tabGO.transform, tabNames[i], Vector2.zero,
                    new Vector2(96, 40), tabLabels[i],
                    i == 0 ? new Color(0.24f, 0.48f, 0.50f) : new Color(0.40f, 0.38f, 0.35f), 18);
                tabBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            }

            // Scroll
            var scrollGO = new GameObject("Scroll");
            scrollGO.transform.SetParent(bg, false);
            var scrollRT = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchoredPosition = new Vector2(0, -15);
            scrollRT.sizeDelta = new Vector2(520, 300);
            scrollGO.AddComponent<UnityEngine.UI.Image>().color = new Color(0.90f, 0.87f, 0.82f);
            scrollGO.AddComponent<Mask>().showMaskGraphic = true;
            var sr = scrollGO.AddComponent<ScrollRect>();
            sr.horizontal = false;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(scrollGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 0);
            var contentVLG = contentGO.AddComponent<VerticalLayoutGroup>();
            contentVLG.spacing = 4;
            contentVLG.padding = new RectOffset(8, 8, 8, 8);
            contentVLG.childForceExpandWidth = true;
            contentVLG.childForceExpandHeight = false;
            contentGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.content = contentRT;
            sr.viewport = scrollRT;

            CreateTextButton(bg, "CloseBtn", new Vector2(0, -220), new Vector2(200, 50),
                "닫기", new Color(0.55f, 0.45f, 0.33f), 22);
        }

        #region Utility

        static GameObject CreateFullPanel(Transform parent, string name, bool active)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            go.SetActive(active);
            return go;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static Transform CreateCenterPanel(Transform parent, string name, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            go.AddComponent<UnityEngine.UI.Image>().color = color;
            return go.transform;
        }

        static void BuildDim(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            Stretch(rt);
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.12f, 0.08f, 0.05f, 0.55f);
            img.raycastTarget = true;
            var btn = go.AddComponent<UnityEngine.UI.Button>();
            btn.transition = Selectable.Transition.None;
        }

        static TextMeshProUGUI CreateTMP(Transform parent, string name, Vector2 pos, Vector2 size,
            string text, int fontSize, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = color;
            return tmp;
        }

        static UnityEngine.UI.Button CreateTextButton(Transform parent, string name, Vector2 pos, Vector2 size,
            string label, Color bgColor, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = bgColor;

            var btn = go.AddComponent<UnityEngine.UI.Button>();
            var colors = btn.colors;
            colors.normalColor = bgColor;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.7f;
            colors.disabledColor = new Color(0.78f, 0.75f, 0.71f, 0.6f);
            btn.colors = colors;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRT = labelGO.AddComponent<RectTransform>();
            Stretch(labelRT);
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }

        static void BuildInputField(Transform parent, string name, Vector2 pos, Vector2 size, string placeholder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            go.AddComponent<UnityEngine.UI.Image>().color = new Color(0.15f, 0.15f, 0.2f, 0.9f);

            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(go.transform, false);
            var taRT = textArea.AddComponent<RectTransform>();
            Stretch(taRT);
            taRT.offsetMin = new Vector2(12, 5);
            taRT.offsetMax = new Vector2(-12, -5);
            textArea.AddComponent<RectMask2D>();

            var phGO = new GameObject("Placeholder");
            phGO.transform.SetParent(textArea.transform, false);
            Stretch(phGO.AddComponent<RectTransform>());
            var phText = phGO.AddComponent<TextMeshProUGUI>();
            phText.text = placeholder;
            phText.fontSize = 20;
            phText.fontStyle = FontStyles.Italic;
            phText.color = new Color(0.61f, 0.56f, 0.50f, 0.7f);
            phText.alignment = TextAlignmentOptions.MidlineLeft;

            var inputTextGO = new GameObject("Text");
            inputTextGO.transform.SetParent(textArea.transform, false);
            Stretch(inputTextGO.AddComponent<RectTransform>());
            var inputTMP = inputTextGO.AddComponent<TextMeshProUGUI>();
            inputTMP.fontSize = 22;
            inputTMP.color = Color.white;
            inputTMP.alignment = TextAlignmentOptions.MidlineLeft;

            var inputField = go.AddComponent<TMP_InputField>();
            inputField.textViewport = taRT;
            inputField.textComponent = inputTMP;
            inputField.placeholder = phText;
            inputField.characterLimit = 8;
            inputField.contentType = TMP_InputField.ContentType.Alphanumeric;
        }

        static void BuildVolumeSlider(Transform parent, string name, Vector2 pos, string label)
        {
            var container = new GameObject(name);
            container.transform.SetParent(parent, false);
            var cRT = container.AddComponent<RectTransform>();
            cRT.anchoredPosition = pos;
            cRT.sizeDelta = new Vector2(400, 50);

            var labelTmp = CreateTMP(container.transform, "Label",
                new Vector2(-150, 0), new Vector2(100, 30), label, 20,
                new Color(0.17f, 0.09f, 0.06f));
            labelTmp.alignment = TextAlignmentOptions.MidlineRight;

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            var sRT = sliderGO.AddComponent<RectTransform>();
            sRT.anchoredPosition = new Vector2(50, 0);
            sRT.sizeDelta = new Vector2(220, 20);

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(sliderGO.transform, false);
            var bgRT = bgGO.AddComponent<RectTransform>();
            Stretch(bgRT);
            bgGO.AddComponent<UnityEngine.UI.Image>().color = new Color(0.78f, 0.75f, 0.71f);

            var fillArea = new GameObject("FillArea");
            fillArea.transform.SetParent(sliderGO.transform, false);
            var faRT = fillArea.AddComponent<RectTransform>();
            Stretch(faRT);
            faRT.offsetMin = new Vector2(5, 0);
            faRT.offsetMax = new Vector2(-5, 0);

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillArea.transform, false);
            var fRT = fillGO.AddComponent<RectTransform>();
            fRT.anchorMin = Vector2.zero;
            fRT.anchorMax = new Vector2(0, 1);
            fRT.offsetMin = Vector2.zero;
            fRT.offsetMax = Vector2.zero;
            fillGO.AddComponent<UnityEngine.UI.Image>().color = new Color(0.24f, 0.48f, 0.50f);

            var handleArea = new GameObject("HandleSlideArea");
            handleArea.transform.SetParent(sliderGO.transform, false);
            var haRT = handleArea.AddComponent<RectTransform>();
            Stretch(haRT);
            haRT.offsetMin = new Vector2(10, -5);
            haRT.offsetMax = new Vector2(-10, 5);

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(handleArea.transform, false);
            var hRT = handleGO.AddComponent<RectTransform>();
            hRT.sizeDelta = new Vector2(20, 30);
            handleGO.AddComponent<UnityEngine.UI.Image>().color = new Color(0.17f, 0.09f, 0.06f);

            var slider = sliderGO.AddComponent<Slider>();
            slider.fillRect = fRT;
            slider.handleRect = hRT;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
        }

        #endregion
    }
}
