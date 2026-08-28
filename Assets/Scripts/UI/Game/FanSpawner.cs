using AbsoluteZero.Core.Common;
using UnityEngine;

namespace AbsoluteZero.UI.Game
{
    public class FanSpawner : MonoBehaviour
    {
        [Header("=== Fan Tuning (live-adjustable in Inspector during play) ===")]
        [SerializeField] float playerFanScale = 0.54f;
        [SerializeField] float enemyFanScale = 0.95f;
        [SerializeField] float fanLiftBase = 1.4f;
        [SerializeField] Vector3 fanHeadCenter = new(-0.16f, 0.60f, 0f);
        [SerializeField] float fanBladeScale = 1.2f;
        [SerializeField] float fanBladeSquashX = 0.82f;
        [SerializeField] Vector2 fanGrilleScale = new(0.92f, 1.02f);
        [SerializeField] float fanBladesZ = -0.015f;
        [SerializeField] float fanGrilleZ = -0.03f;

        public void SpawnStayItemFans()
        {
            var bodySprite = Resources.Load<Sprite>("Fan/fan_body");
            var bladesSprite = Resources.Load<Sprite>("Fan/fan_blades");
            var grilleSprite = Resources.Load<Sprite>("Fan/fan_grille");
            var fallback = GameSprites.GetStayItemSprite();

            SpawnFanAt("PlayerStayItem", true, bodySprite, bladesSprite, grilleSprite, fallback);
            SpawnFanAt("EnemyStayItem", false, bodySprite, bladesSprite, grilleSprite, fallback);
        }

        void SpawnFanAt(string markerName, bool isPlayer,
            Sprite bodySprite, Sprite bladesSprite, Sprite grilleSprite, Sprite fallback)
        {
            var marker = GameObject.Find(markerName);
            if (marker == null) return;

            float s = isPlayer ? playerFanScale : enemyFanScale;
            int playerIndex = isPlayer ? -1 : -2;

            var go = new GameObject($"{markerName}_Fan");
            go.transform.SetParent(marker.transform, false);
            go.transform.localPosition = new Vector3(0f, fanLiftBase * s, 0f);
            go.transform.localScale = isPlayer ? Vector3.one * s : new Vector3(-s, s, s);

            var bodySr = go.AddComponent<SpriteRenderer>();
            bodySr.sprite = bodySprite != null ? bodySprite : fallback;
            bodySr.sortingOrder = 3;

            if (bodySprite == null || bladesSprite == null) return;

            var head = new GameObject("Head");
            head.transform.SetParent(go.transform, false);
            head.transform.localPosition = fanHeadCenter;

            if (grilleSprite != null)
            {
                var grille = new GameObject("Grille");
                grille.transform.SetParent(head.transform, false);
                grille.transform.localPosition = new Vector3(0f, 0f, fanGrilleZ);
                grille.transform.localScale = new Vector3(fanGrilleScale.x, fanGrilleScale.y, 1f);
                var grilleSr = grille.AddComponent<SpriteRenderer>();
                grilleSr.sprite = grilleSprite;
                grilleSr.sortingOrder = 5;
            }

            var pivot = new GameObject("BladePivot");
            pivot.transform.SetParent(head.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0f, fanBladesZ);
            pivot.transform.localScale = new Vector3(fanBladeSquashX, 1f, 1f);

            var blades = new GameObject("Blades");
            blades.transform.SetParent(pivot.transform, false);
            blades.transform.localScale = Vector3.one * fanBladeScale;
            var bladeSr = blades.AddComponent<SpriteRenderer>();
            bladeSr.sprite = bladesSprite;
            bladeSr.sortingOrder = 4;

            var spinner = go.AddComponent<FanBladeSpinner>();
            spinner.Bind(blades.transform, playerIndex);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying) ReapplyFanTuning();
        }

        void ReapplyFanTuning()
        {
            ReapplyFanOne("PlayerStayItem_Fan", true);
            ReapplyFanOne("EnemyStayItem_Fan", false);
        }

        void ReapplyFanOne(string fanName, bool isPlayer)
        {
            var go = GameObject.Find(fanName);
            if (go == null) return;

            float s = isPlayer ? playerFanScale : enemyFanScale;
            go.transform.localPosition = new Vector3(0f, fanLiftBase * s, 0f);
            go.transform.localScale = isPlayer ? Vector3.one * s : new Vector3(-s, s, s);

            var head = go.transform.Find("Head");
            if (head == null) return;
            head.localPosition = fanHeadCenter;

            var grille = head.Find("Grille");
            if (grille != null)
            {
                grille.localPosition = new Vector3(0f, 0f, fanGrilleZ);
                grille.localScale = new Vector3(fanGrilleScale.x, fanGrilleScale.y, 1f);
            }

            var pivot = head.Find("BladePivot");
            if (pivot != null)
            {
                pivot.localPosition = new Vector3(0f, 0f, fanBladesZ);
                pivot.localScale = new Vector3(fanBladeSquashX, 1f, 1f);
                var bladesTf = pivot.Find("Blades");
                if (bladesTf != null) bladesTf.localScale = Vector3.one * fanBladeScale;
            }
        }
#endif
    }
}
