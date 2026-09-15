using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public class CosmeticEquipState
    {
        const string SaveKey = "cosmetic_equip_v1";

        readonly Dictionary<CosmeticPart, CosmeticItemSO> _equipped = new();

        public event Action OnEquipChanged;

        public CosmeticItemSO GetEquipped(CosmeticPart part)
        {
            return _equipped.TryGetValue(part, out var item) ? item : null;
        }

        public void Equip(CosmeticItemSO item)
        {
            if (item == null) return;
            _equipped[item.Part] = item;
            OnEquipChanged?.Invoke();
        }

        public void Unequip(CosmeticPart part)
        {
            if (_equipped.Remove(part))
                OnEquipChanged?.Invoke();
        }

        public CosmeticDto ToDto()
        {
            var dto = new CosmeticDto { v = 1 };
            if (_equipped.TryGetValue(CosmeticPart.Head, out var h)) dto.head = h.Id;
            if (_equipped.TryGetValue(CosmeticPart.Top, out var t)) dto.top = t.Id;
            if (_equipped.TryGetValue(CosmeticPart.Back, out var b)) dto.back = b.Id;
            if (_equipped.TryGetValue(CosmeticPart.Bottom, out var bt)) dto.bottom = bt.Id;
            if (_equipped.TryGetValue(CosmeticPart.Tail, out var tl)) dto.tail = tl.Id;
            return dto;
        }

        public void FromDto(CosmeticDto dto, CosmeticRegistrySO registry)
        {
            _equipped.Clear();
            if (dto == null || registry == null) return;

            TryLoadPart(dto.head, CosmeticPart.Head, registry);
            TryLoadPart(dto.top, CosmeticPart.Top, registry);
            TryLoadPart(dto.back, CosmeticPart.Back, registry);
            TryLoadPart(dto.bottom, CosmeticPart.Bottom, registry);
            TryLoadPart(dto.tail, CosmeticPart.Tail, registry);
        }

        void TryLoadPart(string id, CosmeticPart expectedPart, CosmeticRegistrySO registry)
        {
            if (string.IsNullOrEmpty(id)) return;
            var item = registry.GetById(id);
            if (item == null)
            {
                Debug.LogWarning($"[CosmeticEquipState] Id '{id}' not found in registry — skipping");
                return;
            }
            if (item.Part != expectedPart)
            {
                Debug.LogWarning($"[CosmeticEquipState] Id '{id}' Part mismatch: expected {expectedPart}, got {item.Part} — skipping");
                return;
            }
            _equipped[expectedPart] = item;
        }

        public void Save()
        {
            string json = JsonUtility.ToJson(ToDto());
            PlayerPrefs.SetString(SaveKey, json);
            PlayerPrefs.Save();
        }

        public void Load(CosmeticRegistrySO registry)
        {
            string json = PlayerPrefs.GetString(SaveKey, "");
            if (string.IsNullOrEmpty(json))
            {
                _equipped.Clear();
                return;
            }

            CosmeticDto dto;
            try
            {
                dto = JsonUtility.FromJson<CosmeticDto>(json);
            }
            catch
            {
                Debug.LogWarning("[CosmeticEquipState] Failed to parse saved equip data — resetting");
                _equipped.Clear();
                return;
            }

            if (dto == null || dto.v != 1)
            {
                _equipped.Clear();
                return;
            }

            FromDto(dto, registry);
        }
    }
}
