using System.Collections;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Network;
using AbsoluteZero.Core.Turn;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Loading
{
    public class LoadingScreenManager : MonoBehaviour
    {
        public static LoadingScreenManager Instance { get; private set; }

        struct LoadingTip
        {
            public string ItemName;
            public string DisplayName;
            public string Description;
        }

        static readonly LoadingTip[] _tips =
        {
            new() { ItemName = "Fan", DisplayName = "부채", Description = "상대의 온도를 3° 낮춘다" },
            new() { ItemName = "Windbreaker", DisplayName = "바람막이", Description = "최대 4°의 공격을 방어한다" },
            new() { ItemName = "Warm Tea", DisplayName = "따뜻한 차", Description = "온도를 7° 회복한다" },
            new() { ItemName = "Cat", DisplayName = "고양이", Description = "상대의 랜덤 아이템을 전부 교체한다" },
            new() { ItemName = "Hand Fan", DisplayName = "손풍기", Description = "상대의 온도를 4° 낮춘다" },
            new() { ItemName = "Ice Cream", DisplayName = "아이스크림", Description = "상대의 온도를 5° 낮춘다" },
            new() { ItemName = "Iced Americano", DisplayName = "아이스 아메리카노", Description = "상대의 온도를 5° 낮춘다" },
            new() { ItemName = "Water Gun", DisplayName = "물총", Description = "상대의 온도를 7° 낮춘다" },
            new() { ItemName = "Hug T-shirt", DisplayName = "안아줘요 티셔츠", Description = "상대의 온도를 내 온도와 같게 만든다" },
            new() { ItemName = "Hot Americano", DisplayName = "뜨거운 아메리카노", Description = "온도를 5° 회복한다" },
            new() { ItemName = "Smartphone", DisplayName = "스마트폰", Description = "사용할 때마다 회복량이 증가한다" },
            new() { ItemName = "Hot Pack", DisplayName = "핫팩", Description = "온도를 10° 회복한다" },
            new() { ItemName = "Mask", DisplayName = "마스크", Description = "음식 계열 공격을 완전히 방어한다" },
            new() { ItemName = "Samgyetang", DisplayName = "삼계탕", Description = "상대 온도 +3° 후 다음 턴에 -7°" },
            new() { ItemName = "Soda", DisplayName = "탄산음료", Description = "내 온도 -5° 후 다음 턴에 +15°" },
            new() { ItemName = "Buldak Noodles", DisplayName = "불닭볶음면", Description = "온도를 17° 회복한다" },
            new() { ItemName = "Screwdriver", DisplayName = "십자드라이버", Description = "선풍기의 위력을 2배로 만든다" },
            new() { ItemName = "Tarot Card", DisplayName = "속마음 타로카드", Description = "상대가 선택한 아이템을 미리 확인한다" },
            new() { ItemName = "Claw Machine", DisplayName = "집게손", Description = "상대의 아이템 하나를 가져온다" },
            new() { ItemName = "Blue Tape", DisplayName = "청테이프", Description = "상대의 기본 아이템 사용을 봉인한다" },
            new() { ItemName = "Red Card", DisplayName = "레드카드", Description = "상대의 랜덤 아이템 하나를 버린다" },
        };

        Canvas _canvas;
        CanvasGroup _canvasGroup;
        Image _fillImage;
        TextMeshProUGUI _percentText;

        bool _showing;
        bool _dismissing;
        float _progress;
        float _targetProgress;
        bool _sceneCallbackRegistered;

        static readonly WaitForSeconds _waitFadeStep = new(0.016f);

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                SessionManager.OnLoadingShow += Show;
                SessionManager.OnLoadingHide += ForceHide;
            }
            else
            {
                Destroy(this);
                return;
            }
        }

        void Start()
        {
            TryRegisterSceneCallback();
        }

        void Update()
        {
            if (!_sceneCallbackRegistered)
                TryRegisterSceneCallback();

            if (!_showing || _dismissing) return;

            _targetProgress = Mathf.MoveTowards(_targetProgress, 0.85f, 0.22f * Time.unscaledDeltaTime);
            _progress = Mathf.Lerp(_progress, _targetProgress, 6f * Time.unscaledDeltaTime);
            UpdateProgressUI();

            if (TurnManager.Instance != null
                && TurnManager.Instance.CurrentPhase.Value != TurnPhase.WaitingForPlayers)
            {
                StartCoroutine(DismissRoutine());
            }
        }

        void OnDestroy()
        {
            UnregisterSceneCallback();
            SessionManager.OnLoadingShow -= Show;
            SessionManager.OnLoadingHide -= ForceHide;
            if (Instance == this) Instance = null;
        }

        void TryRegisterSceneCallback()
        {
            if (_sceneCallbackRegistered) return;
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SceneManager == null) return;
            nm.SceneManager.OnLoad += OnNetworkSceneLoad;
            _sceneCallbackRegistered = true;
        }

        void UnregisterSceneCallback()
        {
            if (!_sceneCallbackRegistered) return;
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null)
                nm.SceneManager.OnLoad -= OnNetworkSceneLoad;
            _sceneCallbackRegistered = false;
        }

        void OnNetworkSceneLoad(ulong clientId, string sceneName, LoadSceneMode loadSceneMode, AsyncOperation asyncOperation)
        {
            if (sceneName == "GameScene")
                Show();
        }

        public void Show()
        {
            if (_showing) return;
            _showing = true;
            _dismissing = false;
            _progress = 0f;
            _targetProgress = 0f;

            BuildUI();
            Debug.Log("[LoadingScreen] Show");
        }

        void BuildUI()
        {
            var canvasGO = new GameObject("LoadingScreenCanvas");
            canvasGO.transform.SetParent(transform);

            _canvas = canvasGO.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;

            Transform root = canvasGO.transform;

            // Background — full screen dark
            var bg = CreateImage(root, "Background", Vector2.zero, new Vector2(1920, 1080),
                new Color(0.12f, 0.12f, 0.14f));
            var bgRect = bg.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;

            // Pick random tip
            var tip = _tips[Random.Range(0, _tips.Length)];

            // Item icon — center top
            var itemSprite = GameSprites.GetItemSprite(tip.ItemName);
            var iconGO = CreateImage(root, "ItemIcon", new Vector2(0, 140), new Vector2(150, 150), Color.white);
            var iconImg = iconGO.GetComponent<Image>();
            if (itemSprite != null)
            {
                iconImg.sprite = itemSprite;
                iconImg.preserveAspect = true;
            }
            else
            {
                iconImg.color = new Color(0.3f, 0.3f, 0.3f);
            }
            var iconRect = iconGO.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);

            // Item name
            var nameText = CreateTMP(root, "ItemName", new Vector2(0, 30), new Vector2(800, 50),
                tip.DisplayName, 36);
            nameText.color = new Color(0.95f, 0.95f, 0.95f);
            nameText.fontStyle = FontStyles.Bold;
            CenterAnchor(nameText.GetComponent<RectTransform>());

            // Item description
            var descText = CreateTMP(root, "ItemDesc", new Vector2(0, -20), new Vector2(800, 40),
                tip.Description, 26);
            descText.color = new Color(0.75f, 0.75f, 0.78f);
            CenterAnchor(descText.GetComponent<RectTransform>());

            // Loading bar background
            var barBg = CreateImage(root, "BarBg", new Vector2(0, -100), new Vector2(700, 30),
                new Color(0.9f, 0.9f, 0.9f));
            CenterAnchor(barBg.GetComponent<RectTransform>());

            // Loading bar fill
            var barFillGO = new GameObject("BarFill");
            barFillGO.transform.SetParent(barBg.transform, false);
            var fillRect = barFillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            _fillImage = barFillGO.AddComponent<Image>();
            _fillImage.color = new Color(0.78f, 0.15f, 0.15f);

            // Percent text
            _percentText = CreateTMP(root, "Percent", new Vector2(0, -155), new Vector2(200, 40),
                "0%", 30);
            _percentText.color = new Color(0.9f, 0.9f, 0.9f);
            _percentText.fontStyle = FontStyles.Bold;
            CenterAnchor(_percentText.GetComponent<RectTransform>());

            // Character — bottom right (use kid head sprite as placeholder)
            var charSprite = LoadCharacterSprite();
            if (charSprite != null)
            {
                var charGO = CreateImage(root, "Character", new Vector2(-120, 120), new Vector2(140, 140), Color.white);
                var charImg = charGO.GetComponent<Image>();
                charImg.sprite = charSprite;
                charImg.preserveAspect = true;
                var charRect = charGO.GetComponent<RectTransform>();
                charRect.anchorMin = new Vector2(1f, 0f);
                charRect.anchorMax = new Vector2(1f, 0f);
                charRect.pivot = new Vector2(0.5f, 0.5f);
            }

            // "Loading..." text — bottom right
            var loadingText = CreateTMP(root, "LoadingText", new Vector2(-120, 30), new Vector2(200, 40),
                "Loading...", 28);
            loadingText.color = new Color(0.85f, 0.85f, 0.85f);
            var ltRect = loadingText.GetComponent<RectTransform>();
            ltRect.anchorMin = new Vector2(1f, 0f);
            ltRect.anchorMax = new Vector2(1f, 0f);
            ltRect.pivot = new Vector2(0.5f, 0.5f);

            UpdateProgressUI();
        }

        static Sprite LoadCharacterSprite()
        {
            var sprites = Resources.LoadAll<Sprite>("Environment/kid_idle");
            if (sprites == null) return null;
            foreach (var s in sprites)
                if (s.name == "kid_idle_1") return s;
            return sprites.Length > 0 ? sprites[0] : null;
        }

        void UpdateProgressUI()
        {
            if (_fillImage != null)
            {
                var rect = _fillImage.rectTransform;
                rect.anchorMax = new Vector2(Mathf.Clamp01(_progress), 1f);
            }

            if (_percentText != null)
                _percentText.text = $"{Mathf.RoundToInt(_progress * 100)}%";
        }

        public void ForceHide()
        {
            if (!_showing) return;
            Debug.Log("[LoadingScreen] ForceHide — immediate cleanup");
            StopAllCoroutines();
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            _canvas = null;
            _canvasGroup = null;
            _fillImage = null;
            _percentText = null;
            _showing = false;
            _dismissing = false;
        }

        IEnumerator DismissRoutine()
        {
            if (_dismissing) yield break;
            _dismissing = true;

            Debug.Log("[LoadingScreen] Dismiss — filling to 100%");

            // Fill to 100%
            while (_progress < 0.99f)
            {
                _progress = Mathf.MoveTowards(_progress, 1f, 3f * Time.unscaledDeltaTime);
                UpdateProgressUI();
                yield return null;
            }
            _progress = 1f;
            UpdateProgressUI();

            yield return new WaitForSecondsRealtime(0.4f);

            // Fade out
            float alpha = 1f;
            while (alpha > 0.01f)
            {
                alpha -= 3f * Time.unscaledDeltaTime;
                if (_canvasGroup != null) _canvasGroup.alpha = alpha;
                yield return null;
            }

            Debug.Log("[LoadingScreen] Dismiss complete — destroying canvas");
            if (_canvas != null)
                Destroy(_canvas.gameObject);
            _canvas = null;
            _canvasGroup = null;
            _fillImage = null;
            _percentText = null;
            _showing = false;
            _dismissing = false;
        }

        static GameObject CreateImage(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return go;
        }

        static TextMeshProUGUI CreateTMP(Transform parent, string name, Vector2 pos, Vector2 size, string text, int fontSize)
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
            tmp.raycastTarget = false;
            return tmp;
        }

        static void CenterAnchor(RectTransform rt)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
    }
}
