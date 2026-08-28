using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.Core.Combat
{
    /// <summary>
    /// 화면 전체 VFX 오버레이 — 피격 시 서리(background_VFX1), 회복 시 온기(background_VFX2) 프레임 플래시.
    /// 로컬 플레이어 기준으로만 호출 (CombatVFXManager에서 내가 맞을 때/회복할 때). 페이드 인→아웃.
    /// 지연 생성 싱글턴 — 첫 접근 시 자기 캔버스+오버레이를 만든다. (게임 씬 한정, DDOL 아님)
    /// </summary>
    public class ScreenVFXManager : MonoBehaviour
    {
        static ScreenVFXManager _instance;
        public static ScreenVFXManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ScreenVFXManager");
                    _instance = go.AddComponent<ScreenVFXManager>();
                }
                return _instance;
            }
        }

        [SerializeField] float peakAlpha = 0.9f;
        [SerializeField] float fadeInDuration = 0.2f;
        [SerializeField] float holdDuration = 0.16f;
        [SerializeField] float fadeOutDuration = 0.7f;

        RawImage _frost;   // 피격 (서리)
        RawImage _warm;    // 회복 (온기)
        RawImage _vignette; // 피격 비네트
        Texture2D _vignetteTexture;
        Coroutine _flash;

        void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            BuildOverlay();
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_vignetteTexture != null) Destroy(_vignetteTexture);
        }

        void BuildOverlay()
        {
            var canvasGO = new GameObject("ScreenVFXCanvas");
            canvasGO.transform.SetParent(transform, false);
            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            _frost = CreateFullScreen(canvasGO.transform, "Frost", "background_VFX1");
            _vignette = CreateFullScreen(canvasGO.transform, "Vignette", null);
            _vignetteTexture = GenerateVignetteTexture(128);
            _vignette.texture = _vignetteTexture;
            _warm = CreateFullScreen(canvasGO.transform, "Warm", "background_VFX2");
        }

        static Texture2D GenerateVignetteTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01((dist - 0.5f) / 0.5f);
                alpha *= alpha;
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, alpha));
            }
            tex.Apply();
            return tex;
        }

        static RawImage CreateFullScreen(Transform parent, string name, string resourceTex)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var ri = go.AddComponent<RawImage>();
            ri.texture = Resources.Load<Texture2D>(resourceTex);
            ri.raycastTarget = false;
            ri.color = new Color(1f, 1f, 1f, 0f);   // 평소 투명
            return ri;
        }

        public void PlayHitVFX()
        {
            Flash(_frost, _vignette);
        }

        public void PlayRecoveryVFX()
        {
            Flash(_warm, null);
        }

        void Flash(RawImage target, RawImage secondary)
        {
            if (target == null) return;
            if (_flash != null) StopCoroutine(_flash);
            _flash = StartCoroutine(FlashRoutine(target, secondary));
        }

        IEnumerator FlashRoutine(RawImage target, RawImage secondary)
        {
            if (_frost != null && _frost != target) _frost.color = new Color(1f, 1f, 1f, 0f);
            if (_warm != null && _warm != target) _warm.color = new Color(1f, 1f, 1f, 0f);
            if (_vignette != null && _vignette != target && _vignette != secondary)
                _vignette.color = new Color(1f, 1f, 1f, 0f);

            float t = 0f;
            while (t < fadeInDuration)
            {
                t += Time.deltaTime;
                float a = Mathf.Lerp(0f, peakAlpha, t / fadeInDuration);
                SetAlpha(target, a);
                if (secondary != null) SetAlpha(secondary, a);
                yield return null;
            }
            SetAlpha(target, peakAlpha);
            if (secondary != null) SetAlpha(secondary, peakAlpha);

            if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);

            t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.deltaTime;
                float a = Mathf.Lerp(peakAlpha, 0f, t / fadeOutDuration);
                SetAlpha(target, a);
                if (secondary != null) SetAlpha(secondary, a);
                yield return null;
            }
            SetAlpha(target, 0f);
            if (secondary != null) SetAlpha(secondary, 0f);
            _flash = null;
        }

        static void SetAlpha(RawImage ri, float a)
        {
            if (ri != null) ri.color = new Color(1f, 1f, 1f, a);
        }
    }
}
