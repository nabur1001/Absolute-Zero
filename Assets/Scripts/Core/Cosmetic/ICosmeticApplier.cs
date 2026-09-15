using UnityEngine;

namespace AbsoluteZero.Core.Cosmetic
{
    public interface ICosmeticApplier
    {
        void Apply(Transform partRoot, CosmeticItemSO item, CosmeticPart part);
        void Remove(Transform partRoot, CosmeticPart part);
    }
}
