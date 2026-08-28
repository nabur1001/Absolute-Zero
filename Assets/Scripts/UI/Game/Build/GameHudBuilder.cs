using AbsoluteZero.Core.Common;
using AbsoluteZero.UI.Emote;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game.Build
{
    public static class GameHudBuilder
    {
        static readonly Color INIT_HP_COLOR = new(0.39f, 0.78f, 0.31f, 1f);
        static readonly Vector3 READY_BTN_POS = new(0f, 0.35f, 1.2f);
        const float WORLD_CANVAS_SCALE = 0.005f;
        const float OPP_BAR_SCALE = 0.007f;

        public static GameHudRefs Build()
        {
            var r = new GameHudRefs();
            BuildOverlayUI(r);
            BuildOppBarWorldUI(r);
            BuildReadyWorldUI(r);
            return r;
        }

        static void BuildOverlayUI(GameHudRefs r)
        {
            var canvasGO = new GameObject("OverlayCanvas");
            r.OverlayCanvas = canvasGO.AddComponent<Canvas>();
            r.OverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            r.OverlayCanvas.sortingOrder = 0;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0f;

            canvasGO.AddComponent<GraphicRaycaster>();

            Transform root = canvasGO.transform;

            BuildMyHpBar(r, root);
            BuildClockTimer(r, root);

            r.PhaseText = CreateText(root, "PhaseText",
                new Vector2(0, -30), new Vector2(400, 50), "WAITING", 28);
            AnchorTopCenter(r.PhaseText.GetComponent<RectTransform>());

            BuildProgressHud(r, root);

            r.StatusText = CreateText(root, "StatusText",
                new Vector2(0, 30), new Vector2(600, 35), "Waiting for players...", 20);
            r.StatusText.color = new Color(0.8f, 0.8f, 0.8f);
            AnchorBottomCenter(r.StatusText.GetComponent<RectTransform>());

            BuildEnvironmentPanel(r, root);
            BuildCinematicOverlay(r, root);
        }

        static void BuildMyHpBar(GameHudRefs r, Transform root)
        {
            var container = new GameObject("MyHpBar");
            container.transform.SetParent(root, false);
            var cRect = container.AddComponent<RectTransform>();
            cRect.anchoredPosition = new Vector2(395.8f, -81.9f);
            cRect.sizeDelta = new Vector2(600, 100);
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(0f, 1f);
            cRect.pivot = new Vector2(0.5f, 0.5f);

            r.MyHpSlider = container.AddComponent<Slider>();
            r.MyHpSlider.minValue = 0f;
            r.MyHpSlider.maxValue = 1f;
            r.MyHpSlider.value = 1f;
            r.MyHpSlider.interactable = false;
            r.MyHpSlider.direction = Slider.Direction.LeftToRight;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(container.transform, false);
            var bgRect = bgGO.AddComponent<RectTransform>();
            bgRect.anchorMin = new Vector2(0f, 0.25f);
            bgRect.anchorMax = new Vector2(1f, 0.75f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = GameSprites.Get(GameSprites.UI_BAR_BG);
            bgImg.color = Color.white;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(container.transform, false);
            var faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0f, 0.25f);
            faRect.anchorMax = new Vector2(1f, 0.75f);
            faRect.offsetMin = Vector2.zero;
            faRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillArea.transform, false);
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = GameSprites.Get(GameSprites.UI_BAR_FILL);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.color = INIT_HP_COLOR;
            r.MyHpFillImage = fillImg;

            r.MyHpSlider.fillRect = fillRect;

            var outlineGO = new GameObject("Outline");
            outlineGO.transform.SetParent(container.transform, false);
            var olRect = outlineGO.AddComponent<RectTransform>();
            olRect.anchoredPosition = new Vector2(3.6f, 0f);
            olRect.sizeDelta = new Vector2(620, 80);
            var olImg = outlineGO.AddComponent<Image>();
            olImg.sprite = GameSprites.Get(GameSprites.UI_BAR_OUTLINE);
            olImg.raycastTarget = false;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(container.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchoredPosition = new Vector2(-330.79f, -25.85f);
            iconRect.sizeDelta = new Vector2(100, 200);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = GameSprites.Get(GameSprites.UI_THERMO_ICON);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            BuildGiftLines(container.transform);

            r.MyTempText = CreateText(container.transform, "MyTemp",
                new Vector2(0, -55), new Vector2(200, 24), "37°", 20);
            r.MyTempText.alignment = TextAlignmentOptions.Center;
        }

        static void BuildGiftLines(Transform parent)
        {
            var giftRoot = new GameObject("GiftLine");
            giftRoot.transform.SetParent(parent, false);
            var grRect = giftRoot.AddComponent<RectTransform>();
            grRect.anchorMin = Vector2.zero;
            grRect.anchorMax = Vector2.one;
            grRect.offsetMin = Vector2.zero;
            grRect.offsetMax = Vector2.zero;

            float[] xPositions = { 159.1f, 324.6f, 485.8f };
            string[] iconNames = { GameSprites.UI_GIFT_ICON_A, GameSprites.UI_GIFT_ICON_B, GameSprites.UI_GIFT_ICON_C };
            Vector2[] iconSizes = { new(70, 70), new(70, 35), new(35, 35) };
            float[] iconYOffsets = { -50f, -30f, -30f };

            for (int i = 0; i < 3; i++)
            {
                var lineGO = new GameObject($"line_{(i + 1) * 10}");
                lineGO.transform.SetParent(giftRoot.transform, false);
                var lineRect = lineGO.AddComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(0f, 0.5f);
                lineRect.anchorMax = new Vector2(0f, 0.5f);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                lineRect.anchoredPosition = new Vector2(xPositions[i], 0f);
                lineRect.sizeDelta = new Vector2(7.45f, 70f);
                var lineImg = lineGO.AddComponent<Image>();
                lineImg.sprite = GameSprites.Get(GameSprites.UI_GIFT_LINE);
                lineImg.raycastTarget = false;

                var giftGO = new GameObject("Icon_Gift");
                giftGO.transform.SetParent(lineGO.transform, false);
                var giftRect = giftGO.AddComponent<RectTransform>();
                giftRect.anchoredPosition = new Vector2(0f, iconYOffsets[i]);
                giftRect.sizeDelta = iconSizes[i];
                var giftImg = giftGO.AddComponent<Image>();
                giftImg.sprite = GameSprites.Get(iconNames[i]);
                giftImg.raycastTarget = false;
            }
        }

        static void BuildClockTimer(GameHudRefs r, Transform root)
        {
            var container = new GameObject("Timer");
            container.transform.SetParent(root, false);
            var cRect = container.AddComponent<RectTransform>();
            cRect.anchoredPosition = new Vector2(-108, -108);
            cRect.sizeDelta = new Vector2(204, 206);
            cRect.localScale = new Vector3(0.7f, 0.7f, 1f);
            AnchorTopRight(cRect);
            r.TimerContainerRT = cRect;

            var timerBg = new GameObject("TimerBg");
            timerBg.transform.SetParent(container.transform, false);
            var tbRect = timerBg.AddComponent<RectTransform>();
            tbRect.anchoredPosition = Vector2.zero;
            tbRect.sizeDelta = new Vector2(204, 206);
            r.TimerFillImage = timerBg.AddComponent<Image>();
            r.TimerFillImage.sprite = GameSprites.Get(GameSprites.UI_TIMER_BG);
            r.TimerFillImage.type = Image.Type.Simple;
            r.TimerFillImage.preserveAspect = true;
            r.TimerFillImage.color = Color.white;
            r.TimerFillImage.raycastTarget = false;

            var sliderGO = new GameObject("Slider");
            sliderGO.transform.SetParent(container.transform, false);
            var slRect = sliderGO.AddComponent<RectTransform>();
            slRect.anchoredPosition = Vector2.zero;
            slRect.sizeDelta = new Vector2(204, 206);
            r.TimerSliderImage = sliderGO.AddComponent<Image>();
            r.TimerSliderImage.sprite = GameSprites.Get(GameSprites.UI_TIMER_BG);
            r.TimerSliderImage.type = Image.Type.Filled;
            r.TimerSliderImage.fillMethod = Image.FillMethod.Radial360;
            r.TimerSliderImage.fillOrigin = 2;
            r.TimerSliderImage.fillClockwise = true;
            r.TimerSliderImage.fillAmount = 0f;
            r.TimerSliderImage.color = new Color(0.18f, 0.18f, 0.18f, 1f);
            r.TimerSliderImage.raycastTarget = false;

            var outlineGO = new GameObject("Outline");
            outlineGO.transform.SetParent(container.transform, false);
            var olRect = outlineGO.AddComponent<RectTransform>();
            olRect.anchoredPosition = Vector2.zero;
            olRect.sizeDelta = new Vector2(261, 261);
            var olImg = outlineGO.AddComponent<Image>();
            olImg.sprite = GameSprites.Get(GameSprites.UI_TIMER_OUTLINE);
            olImg.raycastTarget = false;

            var lineGO = new GameObject("Line");
            lineGO.transform.SetParent(container.transform, false);
            var liRect = lineGO.AddComponent<RectTransform>();
            liRect.anchoredPosition = Vector2.zero;
            liRect.sizeDelta = new Vector2(191, 193);
            var liImg = lineGO.AddComponent<Image>();
            liImg.sprite = GameSprites.Get(GameSprites.UI_TIMER_LINE);
            liImg.raycastTarget = false;

            var handGO = new GameObject("ClockHand");
            handGO.transform.SetParent(container.transform, false);
            r.ClockHandRT = handGO.AddComponent<RectTransform>();
            r.ClockHandRT.anchoredPosition = Vector2.zero;
            r.ClockHandRT.sizeDelta = new Vector2(33, 114);
            r.ClockHandRT.pivot = new Vector2(0.5f, 0.15f);
            var handImg = handGO.AddComponent<Image>();
            handImg.sprite = GameSprites.Get(GameSprites.UI_TIMER_HAND);
            handImg.raycastTarget = false;

            r.TimerText = CreateText(container.transform, "TimerText",
                new Vector2(0, -10), new Vector2(120, 60), "", 36);
            r.TimerText.color = Color.white;
            r.TimerText.fontStyle = FontStyles.Bold;

            r.TimeAlarmObj = new GameObject("TimeAlarm");
            r.TimeAlarmObj.transform.SetParent(root, false);
            var alarmRect = r.TimeAlarmObj.AddComponent<RectTransform>();
            alarmRect.anchoredPosition = new Vector2(0, 386);
            alarmRect.sizeDelta = new Vector2(483, 198);
            alarmRect.anchorMin = new Vector2(0.5f, 0.5f);
            alarmRect.anchorMax = new Vector2(0.5f, 0.5f);
            alarmRect.pivot = new Vector2(0.5f, 0.5f);
            r.TimeAlarmImage = r.TimeAlarmObj.AddComponent<Image>();
            r.TimeAlarmImage.sprite = GameSprites.Get(GameSprites.ALARM);
            r.TimeAlarmImage.preserveAspect = true;
            r.TimeAlarmImage.raycastTarget = false;
            r.TimeAlarmObj.SetActive(false);
        }

        static void BuildOppBarWorldUI(GameHudRefs r)
        {
            var canvasGO = new GameObject("EnemyCanvas");
            r.OppBarCanvas = canvasGO.AddComponent<Canvas>();
            r.OppBarCanvas.renderMode = RenderMode.WorldSpace;

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(100, 100);
            canvasGO.transform.localScale = Vector3.one * OPP_BAR_SCALE;

            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var hpBar = new GameObject("HPBar");
            hpBar.transform.SetParent(canvasGO.transform, false);
            var hpRect = hpBar.AddComponent<RectTransform>();
            hpRect.anchoredPosition = Vector2.zero;
            hpRect.sizeDelta = new Vector2(600, 100);

            r.OppHpSlider = hpBar.AddComponent<Slider>();
            r.OppHpSlider.minValue = 0f;
            r.OppHpSlider.maxValue = 1f;
            r.OppHpSlider.value = 1f;
            r.OppHpSlider.interactable = false;
            r.OppHpSlider.direction = Slider.Direction.LeftToRight;

            var bgGO = new GameObject("Background");
            bgGO.transform.SetParent(hpBar.transform, false);
            var bgRect = bgGO.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            var bgImg = bgGO.AddComponent<Image>();
            bgImg.sprite = GameSprites.Get(GameSprites.UI_BAR_BG);
            bgImg.color = Color.white;

            var fillArea = new GameObject("Fill Area");
            fillArea.transform.SetParent(hpBar.transform, false);
            var faRect = fillArea.AddComponent<RectTransform>();
            faRect.anchorMin = new Vector2(0f, 0.25f);
            faRect.anchorMax = new Vector2(1f, 0.75f);
            faRect.offsetMin = Vector2.zero;
            faRect.offsetMax = Vector2.zero;

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(fillArea.transform, false);
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImg = fillGO.AddComponent<Image>();
            fillImg.sprite = GameSprites.Get(GameSprites.UI_BAR_FILL);
            fillImg.type = Image.Type.Filled;
            fillImg.fillMethod = Image.FillMethod.Horizontal;
            fillImg.color = INIT_HP_COLOR;
            r.OppHpFillImage = fillImg;

            r.OppHpSlider.fillRect = fillRect;

            var outlineGO = new GameObject("Outline");
            outlineGO.transform.SetParent(hpBar.transform, false);
            var olRect = outlineGO.AddComponent<RectTransform>();
            olRect.anchoredPosition = new Vector2(3.6f, 0f);
            olRect.sizeDelta = new Vector2(620, 80);
            var olImg = outlineGO.AddComponent<Image>();
            olImg.sprite = GameSprites.Get(GameSprites.UI_BAR_OUTLINE);
            olImg.raycastTarget = false;

            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(hpBar.transform, false);
            var iconRect = iconGO.AddComponent<RectTransform>();
            iconRect.anchoredPosition = new Vector2(-302f, -26f);
            iconRect.sizeDelta = new Vector2(100, 200);
            iconRect.localScale = new Vector3(-1f, 1f, 1f);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = GameSprites.Get(GameSprites.UI_THERMO_ICON);
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            BuildOppDividerLines(hpBar.transform);

            r.OppTempText = CreateText(canvasGO.transform, "OppTemp",
                new Vector2(0, -70), new Vector2(360, 32), "37°", 26);
        }

        static void BuildOppDividerLines(Transform parent)
        {
            var root = new GameObject("DividerLines");
            root.transform.SetParent(parent, false);
            var rootRect = root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            float[] xPositions = { 159.1f, 324.6f, 485.8f };
            for (int i = 0; i < 3; i++)
            {
                var lineGO = new GameObject($"line_{(i + 1) * 10}");
                lineGO.transform.SetParent(root.transform, false);
                var lineRect = lineGO.AddComponent<RectTransform>();
                lineRect.anchorMin = new Vector2(0f, 0.5f);
                lineRect.anchorMax = new Vector2(0f, 0.5f);
                lineRect.pivot = new Vector2(0.5f, 0.5f);
                lineRect.anchoredPosition = new Vector2(xPositions[i], 0f);
                lineRect.sizeDelta = new Vector2(7.45f, 70f);
                var lineImg = lineGO.AddComponent<Image>();
                lineImg.sprite = GameSprites.Get(GameSprites.UI_GIFT_LINE);
                lineImg.raycastTarget = false;
            }
        }

        static void BuildReadyWorldUI(GameHudRefs r)
        {
            var canvasGO = new GameObject("ReadyWorldCanvas");
            r.ReadyCanvas = canvasGO.AddComponent<Canvas>();
            r.ReadyCanvas.renderMode = RenderMode.WorldSpace;

            var rt = canvasGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(300, 180);
            canvasGO.transform.localScale = Vector3.one * WORLD_CANVAS_SCALE;
            canvasGO.transform.position = READY_BTN_POS;
            canvasGO.transform.rotation = Quaternion.Euler(60f, 0f, 0f);

            canvasGO.AddComponent<GraphicRaycaster>();

            var readySprite = GameSprites.Get(GameSprites.BTN_DEFAULT);
            var btnGO = new GameObject("ReadyBtn");
            btnGO.transform.SetParent(canvasGO.transform, false);
            var btnRect = btnGO.AddComponent<RectTransform>();
            btnRect.anchoredPosition = Vector2.zero;
            btnRect.sizeDelta = new Vector2(560, 320);

            var btnImg = btnGO.AddComponent<Image>();
            if (readySprite != null)
            {
                btnImg.sprite = readySprite;
                btnImg.preserveAspect = true;
            }
            else
            {
                btnImg.color = new Color(0.5f, 0.5f, 0.2f);
            }

            r.ReadyButtonImage = btnImg;
            r.ReadyButton = btnGO.AddComponent<Button>();
            r.ReadyButton.targetGraphic = btnImg;
            btnGO.AddComponent<EmoteWheel>();
            r.ReadyCanvas.gameObject.SetActive(false);
        }

        static void BuildProgressHud(GameHudRefs r, Transform root)
        {
            var container = new GameObject("ProgressHud");
            container.transform.SetParent(root, false);
            var cRect = container.AddComponent<RectTransform>();
            cRect.anchoredPosition = new Vector2(0, -75);
            cRect.sizeDelta = new Vector2(400, 30);
            AnchorTopCenter(cRect);

            var boxColor = new Color(0.15f, 0.15f, 0.2f, 0.8f);

            r.P1NameBox = CreatePanel(container.transform, "P1Box",
                new Vector2(-145, 0), new Vector2(130, 28), boxColor);
            r.P1NameText = CreateText(r.P1NameBox.transform, "P1Name",
                Vector2.zero, new Vector2(120, 24), "Player 1", 14);
            r.P1NameText.alignment = TextAlignmentOptions.Center;

            r.ScoreText = CreateText(container.transform, "ScoreText",
                Vector2.zero, new Vector2(80, 30), "0 : 0", 20);
            r.ScoreText.color = new Color(0.9f, 0.9f, 0.6f);

            r.P2NameBox = CreatePanel(container.transform, "P2Box",
                new Vector2(145, 0), new Vector2(130, 28), boxColor);
            r.P2NameText = CreateText(r.P2NameBox.transform, "P2Name",
                Vector2.zero, new Vector2(120, 24), "Player 2", 14);
            r.P2NameText.alignment = TextAlignmentOptions.Center;

            r.CrownText = CreateText(container.transform, "Crown",
                new Vector2(-80, 0), new Vector2(24, 24), "♛", 18);
            r.CrownText.color = new Color(1f, 0.85f, 0.2f);
            r.CrownText.gameObject.SetActive(false);
        }

        static void BuildCinematicOverlay(GameHudRefs r, Transform root)
        {
            var overlayGO = new GameObject("CinematicOverlay");
            overlayGO.transform.SetParent(root, false);
            var oRect = overlayGO.AddComponent<RectTransform>();
            oRect.anchorMin = Vector2.zero;
            oRect.anchorMax = Vector2.one;
            oRect.offsetMin = Vector2.zero;
            oRect.offsetMax = Vector2.zero;
            r.CinematicOverlay = overlayGO.AddComponent<Image>();
            r.CinematicOverlay.color = new Color(0f, 0f, 0f, 0f);
            r.CinematicOverlay.raycastTarget = false;

            r.CinematicText = CreateText(overlayGO.transform, "CinematicText",
                new Vector2(0, 20), new Vector2(800, 100), "", 64);
            r.CinematicText.fontStyle = FontStyles.Bold;
            r.CinematicText.color = new Color(1f, 1f, 1f, 0f);

            r.LobbyButton = CreateButton(overlayGO.transform, "LobbyBtn",
                new Vector2(0, -60), new Vector2(200, 45), "BACK TO LOBBY",
                new Color(0.5f, 0.3f, 0.3f));
            r.LobbyButton.gameObject.SetActive(false);

            overlayGO.SetActive(false);
        }

        static void BuildEnvironmentPanel(GameHudRefs r, Transform root)
        {
            r.EnvPanel = new GameObject("EnvPanel");
            r.EnvPanel.transform.SetParent(root, false);
            var rect = r.EnvPanel.AddComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(0, -140);
            rect.sizeDelta = new Vector2(600, 80);
            AnchorTopCenter(rect);

            CreatePanel(r.EnvPanel.transform, "EnvBg",
                Vector2.zero, new Vector2(600, 80),
                new Color(0.1f, 0.05f, 0.2f, 0.9f));

            r.EnvText = CreateText(r.EnvPanel.transform, "EnvText",
                Vector2.zero, new Vector2(560, 60), "", 32);
            r.EnvText.fontStyle = FontStyles.Bold;
            r.EnvText.color = new Color(1f, 0.85f, 0.3f);

            r.EnvPanel.SetActive(false);
        }

        public static void AnchorTopCenter(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
        }

        public static void AnchorTopLeft(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
        }

        public static void AnchorTopRight(RectTransform rt)
        {
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
        }

        public static void AnchorBottomLeft(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
        }

        public static void AnchorBottomCenter(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
        }

        public static TextMeshProUGUI CreateText(Transform parent, string name,
            Vector2 pos, Vector2 size, string text, int fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            return tmp;
        }

        public static Image CreatePanel(Transform parent, string name,
            Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Button CreateButton(Transform parent, string name,
            Vector2 pos, Vector2 size, string label, Color bgColor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = bgColor;

            var btn = go.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = bgColor * 1.2f;
            colors.pressedColor = bgColor * 0.7f;
            colors.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
            btn.colors = colors;

            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(go.transform, false);
            var labelRect = labelGO.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var tmp = labelGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 18;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return btn;
        }
    }
}
