using UnityEngine;
using UnityEngine.UI;

namespace AbsoluteZero.UI.Game.Presenters
{
    /// <summary>
    /// Client-local targeting ribbon. It renders in screen space and never owns gameplay state.
    /// </summary>
    public sealed class TargetingArrowPresenter : MonoBehaviour
    {
        Canvas _canvas;
        RectTransform _canvasRect;
        TargetingArrowGraphic _graphic;

        public bool IsVisible => _graphic != null && _graphic.gameObject.activeSelf;

        public void Initialize()
        {
            if (_canvas != null) return;

            var canvasObject = new GameObject("TargetingArrowCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = -10;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = canvasObject.GetComponent<RectTransform>();

            var graphicObject = new GameObject("TargetingRibbon", typeof(RectTransform), typeof(TargetingArrowGraphic));
            graphicObject.transform.SetParent(canvasObject.transform, false);
            var rect = graphicObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _graphic = graphicObject.GetComponent<TargetingArrowGraphic>();
            _graphic.raycastTarget = false;
            _graphic.gameObject.SetActive(false);
        }

        public void Show(Vector2 tailScreen, Vector2 headScreen, bool straight, float bendDirection)
        {
            Initialize();
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, tailScreen, null, out var tail)
                || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, headScreen, null, out var head))
            {
                Hide();
                return;
            }

            _graphic.SetPath(tail, head, straight, Mathf.Approximately(bendDirection, 0f) ? 1f : Mathf.Sign(bendDirection));
            _graphic.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (_graphic != null)
                _graphic.gameObject.SetActive(false);
        }
    }

    [RequireComponent(typeof(CanvasRenderer))]
    sealed class TargetingArrowGraphic : MaskableGraphic
    {
        const int SegmentCount = 28;
        static readonly Color32 OutlineColor = new(70, 34, 5, 255);
        static readonly Color32 FillColor = new(255, 202, 18, 255);

        Vector2 _tail;
        Vector2 _head;
        bool _straight;
        float _bendDirection = 1f;

        public void SetPath(Vector2 tail, Vector2 head, bool straight, float bendDirection)
        {
            _tail = tail;
            _head = head;
            _straight = straight;
            _bendDirection = bendDirection;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 delta = _head - _tail;
            float distance = delta.magnitude;
            if (distance < 24f) return;

            Vector2 direction = delta / distance;
            float outerHeadLength = Mathf.Clamp(distance * 0.16f, 34f, 62f);
            Vector2 curveEnd = _head - direction * outerHeadLength * 0.62f;
            GetControls(_tail, curveEnd, distance, out var c1, out var c2);

            AddRibbon(vh, _tail, c1, c2, curveEnd, 34f, OutlineColor);
            AddRibbon(vh, _tail, c1, c2, curveEnd, 23f, FillColor);

            Vector2 tangent = (curveEnd - Cubic(_tail, c1, c2, curveEnd, 0.94f)).normalized;
            if (tangent.sqrMagnitude < 0.01f) tangent = direction;
            Vector2 normal = new(-tangent.y, tangent.x);
            AddTriangle(vh, _head, curveEnd - tangent * 9f + normal * 34f,
                curveEnd - tangent * 9f - normal * 34f, OutlineColor);
            AddTriangle(vh, _head - tangent * 5f, curveEnd + normal * 23f,
                curveEnd - normal * 23f, FillColor);
        }

        void GetControls(Vector2 start, Vector2 end, float distance, out Vector2 c1, out Vector2 c2)
        {
            if (_straight)
            {
                c1 = Vector2.Lerp(start, end, 0.34f);
                c2 = Vector2.Lerp(start, end, 0.68f);
                return;
            }

            float bend = Mathf.Clamp(distance * 0.58f, 150f, 430f) * _bendDirection;
            float rise = Mathf.Max(80f, Mathf.Abs(end.y - start.y) * 0.42f);
            c1 = start + new Vector2(bend, rise);
            c2 = end + new Vector2(bend * 0.72f, -rise * 0.28f);
        }

        static void AddRibbon(VertexHelper vh, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
            float width, Color32 color)
        {
            int first = vh.currentVertCount;
            float half = width * 0.5f;
            for (int i = 0; i <= SegmentCount; i++)
            {
                float t = i / (float)SegmentCount;
                Vector2 point = Cubic(p0, p1, p2, p3, t);
                Vector2 tangent = CubicTangent(p0, p1, p2, p3, t).normalized;
                if (tangent.sqrMagnitude < 0.01f) tangent = Vector2.up;
                Vector2 normal = new(-tangent.y, tangent.x);
                vh.AddVert(point + normal * half, color, Vector2.zero);
                vh.AddVert(point - normal * half, color, Vector2.zero);
                if (i == 0) continue;
                int current = first + i * 2;
                int previous = current - 2;
                vh.AddTriangle(previous, previous + 1, current);
                vh.AddTriangle(current, previous + 1, current + 1);
            }
        }

        static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            int first = vh.currentVertCount;
            vh.AddVert(a, color, Vector2.zero);
            vh.AddVert(b, color, Vector2.zero);
            vh.AddVert(c, color, Vector2.zero);
            vh.AddTriangle(first, first + 1, first + 2);
        }

        static Vector2 Cubic(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        static Vector2 CubicTangent(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        }
    }
}
