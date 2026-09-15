using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public class OverlayApplier : ICosmeticApplier
    {
        public void Apply(Transform partRoot, CosmeticItemSO item, CosmeticPart part)
        {
            if (partRoot == null || item == null) return;

            string childName = $"_cosmetic_overlay_{part}";
            Transform existing = partRoot.Find(childName);

            var baseSr = partRoot.GetComponent<SpriteRenderer>();

            if (existing != null)
            {
                existing.gameObject.SetActive(true);
                var sr = existing.GetComponent<SpriteRenderer>();
                if (sr != null && baseSr != null)
                {
                    sr.sortingLayerID = baseSr.sortingLayerID;
                    sr.material = baseSr.material;
                    sr.flipX = baseSr.flipX;
                    sr.flipY = baseSr.flipY;
                }
                if (sr != null)
                {
                    sr.sprite = item.Sprite;
                    sr.sortingOrder = (baseSr != null ? baseSr.sortingOrder : 0) + item.SortOrderOffset;
                }
            }
            else
            {
                var go = new GameObject(childName);
                go.transform.SetParent(partRoot, false);
                var sr = go.AddComponent<SpriteRenderer>();

                if (baseSr != null)
                {
                    sr.sortingLayerID = baseSr.sortingLayerID;
                    sr.material = baseSr.material;
                    sr.flipX = baseSr.flipX;
                    sr.flipY = baseSr.flipY;
                    sr.sortingOrder = baseSr.sortingOrder + item.SortOrderOffset;
                }
                else
                {
                    sr.sortingOrder = item.SortOrderOffset;
                }

                sr.sprite = item.Sprite;
            }
        }

        public void Remove(Transform partRoot, CosmeticPart part)
        {
            if (partRoot == null) return;
            string childName = $"_cosmetic_overlay_{part}";
            Transform existing = partRoot.Find(childName);
            if (existing != null)
                existing.gameObject.SetActive(false);
        }
    }
}
