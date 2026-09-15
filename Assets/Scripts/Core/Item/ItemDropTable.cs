using System;
using System.Collections.Generic;
using AbsoluteZero.Core.Item.Data;
using UnityEngine;

namespace AbsoluteZero.Core.Item
{
    public class ItemDropTable
    {
        struct WeightedItem
        {
            public ItemDataSO Item;
            public float Weight;
        }

        WeightedItem[] _entries;
        float _totalWeight;

        public ItemDropTable(ItemDataSO[] allItems) : this(allItems, null) { }

        public ItemDropTable(ItemDataSO[] allItems, Predicate<ItemDataSO> filter)
        {
            if (allItems == null || allItems.Length == 0)
            {
                _entries = Array.Empty<WeightedItem>();
                _totalWeight = 0f;
                return;
            }

            var list = new List<WeightedItem>(allItems.Length);
            _totalWeight = 0f;

            for (int i = 0; i < allItems.Length; i++)
            {
                var item = allItems[i];
                if (item != null && item.DropWeight > 0f && (filter == null || filter(item)))
                {
                    list.Add(new WeightedItem { Item = item, Weight = item.DropWeight });
                    _totalWeight += item.DropWeight;
                }
            }

            _entries = list.Count > 0 ? list.ToArray() : Array.Empty<WeightedItem>();
        }

        public bool IsEmpty => _entries.Length == 0;

        public ItemDataSO Roll(Predicate<ItemDataSO> eligibility)
        {
            if (_entries.Length == 0 || eligibility == null) return null;

            float eligibleWeight = 0f;
            for (int i = 0; i < _entries.Length; i++)
                if (eligibility(_entries[i].Item))
                    eligibleWeight += _entries[i].Weight;

            if (eligibleWeight <= 0f) return null;

            float roll = UnityEngine.Random.Range(0f, eligibleWeight);
            ItemDataSO lastEligible = null;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (!eligibility(_entries[i].Item)) continue;
                lastEligible = _entries[i].Item;
                roll -= _entries[i].Weight;
                if (roll <= 0f) return lastEligible;
            }

            return lastEligible;
        }

        public ItemDataSO Roll()
        {
            if (_entries.Length == 0) return null;

            float roll = UnityEngine.Random.Range(0f, _totalWeight);
            float cumulative = 0f;

            for (int i = 0; i < _entries.Length; i++)
            {
                cumulative += _entries[i].Weight;
                if (roll <= cumulative) return _entries[i].Item;
            }

            return _entries[_entries.Length - 1].Item;
        }
    }
}
