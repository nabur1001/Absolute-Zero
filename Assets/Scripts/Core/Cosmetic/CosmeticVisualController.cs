using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public class CosmeticVisualController
    {
        readonly Dictionary<CosmeticPart, Transform> _partRoots = new();
        readonly OverlayApplier _overlayApplier = new();
        readonly SwapApplier _swapApplier = new();

        public void SetPartRoot(CosmeticPart part, Transform root)
        {
            _partRoots[part] = root;
        }

        public void ApplyAll(CosmeticEquipState equipState)
        {
            Clear();
            if (equipState == null) return;

            foreach (var kvp in _partRoots)
            {
                var item = equipState.GetEquipped(kvp.Key);
                if (item == null) continue;
                GetApplier(item.Type).Apply(kvp.Value, item, kvp.Key);
            }
        }

        public void ApplyFromDto(string json, CosmeticRegistrySO registry)
        {
            Clear();
            if (string.IsNullOrEmpty(json) || registry == null) return;

            CosmeticDto dto;
            try
            {
                dto = JsonUtility.FromJson<CosmeticDto>(json);
            }
            catch
            {
                return;
            }

            if (dto == null) return;

            ApplyPartFromDto(dto.head, CosmeticPart.Head, registry);
            ApplyPartFromDto(dto.top, CosmeticPart.Top, registry);
            ApplyPartFromDto(dto.back, CosmeticPart.Back, registry);
            ApplyPartFromDto(dto.bottom, CosmeticPart.Bottom, registry);
            ApplyPartFromDto(dto.tail, CosmeticPart.Tail, registry);
        }

        public void Clear()
        {
            foreach (var kvp in _partRoots)
            {
                _overlayApplier.Remove(kvp.Value, kvp.Key);
                _swapApplier.Remove(kvp.Value, kvp.Key);
            }
        }

        void ApplyPartFromDto(string id, CosmeticPart part, CosmeticRegistrySO registry)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (!_partRoots.TryGetValue(part, out var root)) return;

            var item = registry.GetById(id);
            if (item == null || item.Part != part) return;

            GetApplier(item.Type).Apply(root, item, part);
        }

        ICosmeticApplier GetApplier(CosmeticType type)
        {
            return type == CosmeticType.Swap ? _swapApplier : _overlayApplier;
        }
    }
}
