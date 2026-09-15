using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public class SwapApplier : ICosmeticApplier
    {
        readonly Dictionary<(Transform, CosmeticPart), Sprite> _originals = new();

        public void Apply(Transform partRoot, CosmeticItemSO item, CosmeticPart part)
        {
            if (partRoot == null || item == null) return;

            var sr = partRoot.GetComponent<SpriteRenderer>();
            if (sr == null) return;

            var key = (partRoot, part);
            if (!_originals.ContainsKey(key))
                _originals[key] = sr.sprite;

            sr.sprite = item.Sprite;
        }

        public void Remove(Transform partRoot, CosmeticPart part)
        {
            if (partRoot == null) return;

            var key = (partRoot, part);
            if (_originals.TryGetValue(key, out var original))
            {
                var sr = partRoot.GetComponent<SpriteRenderer>();
                if (sr != null)
                    sr.sprite = original;
                _originals.Remove(key);
            }
        }
    }
}
