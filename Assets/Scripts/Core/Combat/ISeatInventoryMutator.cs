using System;
using AbsoluteZero.Core.Item;

namespace AbsoluteZero.Core.Combat
{
    public sealed class InventoryMutationPlan
    {
        readonly Func<bool> _commit;
        bool _applied;

        public InventoryMutationType Type { get; }
        public bool IsNoOp { get; }

        internal InventoryMutationPlan(InventoryMutationType type, bool isNoOp, Func<bool> commit)
        {
            Type = type;
            IsNoOp = isNoOp;
            _commit = commit;
        }

        public bool TryApply()
        {
            if (_applied) return false;
            _applied = true;
            return _commit == null || _commit();
        }
    }

    public interface ISeatInventoryMutator
    {
        bool CanConsume(byte seat, byte slot, short itemId);
        bool TryConsume(byte seat, byte slot, short itemId);
        bool TryPrepareMutation(InventoryMutationType type, byte actorSeat, byte targetSeat,
            out InventoryMutationPlan plan);
    }
}
