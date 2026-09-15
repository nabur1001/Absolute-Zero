using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public enum CosmeticPart { Head, Top, Back, Bottom, Tail }
    public enum CosmeticType { Overlay, Swap }

    [CreateAssetMenu(fileName = "NewCosmeticItem", menuName = "AbsoluteZero/Cosmetic Item")]
    public class CosmeticItemSO : ScriptableObject
    {
        [SerializeField] string _id;
        public string Id => _id;

        [SerializeField] CosmeticPart _part;
        public CosmeticPart Part => _part;

        [SerializeField] CosmeticType _type;
        public CosmeticType Type => _type;

        [SerializeField] Sprite _sprite;
        public Sprite Sprite => _sprite;

        [SerializeField] int _sortOrderOffset;
        public int SortOrderOffset => _sortOrderOffset;

        [SerializeField] string _displayName;
        public string DisplayName => _displayName;
    }
}
