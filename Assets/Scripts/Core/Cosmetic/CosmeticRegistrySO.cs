using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    [CreateAssetMenu(fileName = "CosmeticRegistry", menuName = "AbsoluteZero/Cosmetic Registry")]
    public class CosmeticRegistrySO : ScriptableObject
    {
        [SerializeField] List<CosmeticItemSO> _allItems = new();

        static readonly Regex IdRegex = new(@"^[a-z0-9_]{1,8}$", RegexOptions.Compiled);

        static readonly Dictionary<CosmeticPart, HashSet<CosmeticType>> AllowedTypes = new()
        {
            { CosmeticPart.Head, new() { CosmeticType.Overlay } },
            { CosmeticPart.Top, new() { CosmeticType.Overlay, CosmeticType.Swap } },
            { CosmeticPart.Back, new() { CosmeticType.Overlay } },
            { CosmeticPart.Bottom, new() { CosmeticType.Overlay, CosmeticType.Swap } },
            { CosmeticPart.Tail, new() { CosmeticType.Overlay } },
        };

        List<CosmeticItemSO> _validItems;
        Dictionary<string, CosmeticItemSO> _idLookup;
        Dictionary<CosmeticPart, List<CosmeticItemSO>> _partLookup;

        void OnEnable()
        {
            RebuildLookups();
        }

        void RebuildLookups()
        {
            _validItems = new List<CosmeticItemSO>();
            _idLookup = new Dictionary<string, CosmeticItemSO>();
            _partLookup = new Dictionary<CosmeticPart, List<CosmeticItemSO>>();

            var idCounts = new Dictionary<string, int>();
            foreach (var item in _allItems)
            {
                if (item == null) continue;
                string id = item.Id;
                if (string.IsNullOrEmpty(id)) continue;
                idCounts.TryGetValue(id, out int count);
                idCounts[id] = count + 1;
            }

            var duplicateIds = new HashSet<string>();
            foreach (var kvp in idCounts)
            {
                if (kvp.Value > 1)
                {
                    duplicateIds.Add(kvp.Key);
                    Debug.LogError($"[CosmeticRegistry] Duplicate Id '{kvp.Key}' ({kvp.Value} items) — all excluded");
                }
            }

            foreach (var item in _allItems)
            {
                if (item == null)
                {
                    Debug.LogError("[CosmeticRegistry] Null item in registry");
                    continue;
                }

                string id = item.Id;
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogError($"[CosmeticRegistry] Empty Id on '{item.name}'");
                    continue;
                }

                if (!IdRegex.IsMatch(id))
                {
                    Debug.LogError($"[CosmeticRegistry] Invalid Id format '{id}' on '{item.name}'");
                    continue;
                }

                if (duplicateIds.Contains(id)) continue;

                if (AllowedTypes.TryGetValue(item.Part, out var allowed) && !allowed.Contains(item.Type))
                {
                    Debug.LogError($"[CosmeticRegistry] '{id}': Type {item.Type} not allowed for Part {item.Part}");
                    continue;
                }

                _validItems.Add(item);
                _idLookup[id] = item;

                if (!_partLookup.TryGetValue(item.Part, out var list))
                {
                    list = new List<CosmeticItemSO>();
                    _partLookup[item.Part] = list;
                }
                list.Add(item);
            }
        }

        public CosmeticItemSO GetById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_idLookup == null) RebuildLookups();
            return _idLookup.TryGetValue(id, out var item) ? item : null;
        }

        public List<CosmeticItemSO> GetByPart(CosmeticPart part)
        {
            if (_partLookup == null) RebuildLookups();
            return _partLookup.TryGetValue(part, out var list) ? list : new List<CosmeticItemSO>();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            var ids = new HashSet<string>();
            foreach (var item in _allItems)
            {
                if (item == null) continue;
                string id = item.Id;
                if (string.IsNullOrEmpty(id))
                {
                    Debug.LogError($"[CosmeticRegistry] Empty Id on '{item.name}'");
                    continue;
                }
                if (!IdRegex.IsMatch(id))
                    Debug.LogError($"[CosmeticRegistry] Invalid Id format '{id}' on '{item.name}'");
                if (!ids.Add(id))
                    Debug.LogError($"[CosmeticRegistry] Duplicate Id '{id}' on '{item.name}'");
                if (AllowedTypes.TryGetValue(item.Part, out var allowed) && !allowed.Contains(item.Type))
                    Debug.LogError($"[CosmeticRegistry] '{id}': Type {item.Type} not allowed for Part {item.Part}");
            }
        }
#endif
    }
}
