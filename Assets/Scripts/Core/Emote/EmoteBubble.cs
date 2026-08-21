using System.Collections;
using AbsoluteZero.Core.Player;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace AbsoluteZero.Core.Emote
{
    public class EmoteBubble : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void BindEvents()
        {
            _pool = null;
            PlayerState.OnEmoteRequested += (anchor, pos, id) => Show(anchor, pos, id);
        }

        const float BubbleW = 190f;
        const float BubbleH = 210f;
        const float HeadWorldUp = 1.9f;
        const float ScreenUp = 30f;
        const float ScreenX = -30f;

        Camera _cam;
        Transform _anchor;
        Vector3 _worldPos;
        Canvas _canvas;
        RectTransform _area;
        RectTransform _root;
        CanvasGroup _cg;
        Image _textImage;
        Image _charImage;

        static ObjectPool<EmoteBubble> _pool;

        static readonly WaitForSeconds _waitHold = new(0.55f);

        static ObjectPool<EmoteBubble> Pool => _pool ??= new ObjectPool<EmoteBubble>(
            createFunc: () =>
            {
                var go = new GameObject("EmoteBubble");
                DontDestroyOnLoad(go);
                var b = go.AddComponent<EmoteBubble>();
                b.BuildHierarchy();
                go.SetActive(false);
                return b;
            },
            actionOnGet: b => b.gameObject.SetActive(true),
            actionOnRelease: b =>
            {
                b.StopAllCoroutines();
                b._anchor = null;
                b.gameObject.SetActive(false);
            },
            actionOnDestroy: b =>
            {
                if (b != null) Destroy(b.gameObject);
            },
            defaultCapacity: 2,
            maxSize: 5
        );

        public static void Show(Transform anchor, Vector3 worldFallback, int emoteId)
        {
            var b = Pool.Get();
            b._anchor = anchor;
            b._worldPos = worldFallback;
            b.Activate(emoteId);
        }

        void BuildHierarchy()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 90;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var areaGO = new GameObject("Area");
            areaGO.transform.SetParent(_canvas.transform, false);
            _area = areaGO.AddComponent<RectTransform>();
            _area.anchorMin = Vector2.zero;
            _area.anchorMax = Vector2.one;
            _area.offsetMin = Vector2.zero;
            _area.offsetMax = Vector2.zero;

            var rootGO = new GameObject("Bubble");
            rootGO.transform.SetParent(_area, false);
            _root = rootGO.AddComponent<RectTransform>();
            _root.anchorMin = _root.anchorMax = new Vector2(0.5f, 0.5f);
            _root.pivot = new Vector2(0.5f, 0.1f);
            _root.sizeDelta = new Vector2(BubbleW, BubbleH);
            _cg = rootGO.AddComponent<CanvasGroup>();
            _cg.alpha = 0f;

            _textImage = CreateImage("Text",
                new Vector2(0f, BubbleH * 0.78f),
                new Vector2(BubbleW * 0.9f, 66f));
            _charImage = CreateImage("Char",
                new Vector2(0f, BubbleH * 0.4f),
                new Vector2(BubbleW * 0.86f, BubbleW * 0.68f));
        }

        Image CreateImage(string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        void Activate(int emoteId)
        {
            _cam = Camera.main;

            var textSprite = EmoteCatalog.Text(emoteId);
            var charSprite = EmoteCatalog.Char(emoteId);

            _textImage.sprite = textSprite;
            _textImage.color = textSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);
            _charImage.sprite = charSprite;
            _charImage.color = charSprite != null ? Color.white : new Color(1f, 1f, 1f, 0.2f);

            _cg.alpha = 0f;
            _root.localScale = Vector3.one;

            UpdateFollow();
            StartCoroutine(LifeRoutine());
        }

        void LateUpdate() => UpdateFollow();

        void UpdateFollow()
        {
            if (_root == null || _area == null || _canvas == null)
            {
                Pool.Release(this);
                return;
            }
            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            Vector3 wp = (_anchor != null ? _anchor.position : _worldPos) + Vector3.up * HeadWorldUp;
            Vector3 sp = _cam.WorldToScreenPoint(wp);
            bool visible = sp.z > 0f;
            _canvas.enabled = visible;

            if (visible &&
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _area, new Vector2(sp.x, sp.y), null, out var lp))
                _root.anchoredPosition = lp + new Vector2(ScreenX, ScreenUp);
        }

        IEnumerator LifeRoutine()
        {
            const float inDur = 0.16f, outDur = 0.3f;

            float t = 0f;
            while (t < inDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / inDur);
                _cg.alpha = p;
                _root.localScale = Vector3.one * (0.55f + 0.45f * EaseOutBack(p));
                yield return null;
            }
            _cg.alpha = 1f;
            _root.localScale = Vector3.one;

            yield return _waitHold;

            t = 0f;
            while (t < outDur)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / outDur);
                _cg.alpha = 1f - p;
                yield return null;
            }

            Pool.Release(this);
        }

        static float EaseOutBack(float t)
        {
            const float c1 = 1.70158f, c3 = 1.70158f + 1f;
            float p = t - 1f;
            return 1f + c3 * p * p * p + c1 * p * p;
        }
    }
}
