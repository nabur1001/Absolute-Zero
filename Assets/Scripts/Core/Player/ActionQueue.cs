using AbsoluteZero.Core.Item.Data;

namespace AbsoluteZero.Core.Player
{
    public class ActionQueue
    {
        public QueuedAction? selectedAction = null;
        public QueuedAction? subAction = null;
        public float readyTimestamp;
        public bool isReady;
        public bool hasUsedSub;

        public void SetSelected(byte slotIndex, ItemDataSO itemData, byte targetSeat = ActionIntent.NoTarget)
        {
            selectedAction = new QueuedAction(slotIndex, itemData, targetSeat);
        }

        public void SetSub(byte slotIndex, ItemDataSO itemData, byte targetSeat = ActionIntent.NoTarget)
        {
            subAction = new QueuedAction(slotIndex, itemData, targetSeat);
            hasUsedSub = true;
        }

        public void SetReady(float timestamp)
        {
            readyTimestamp = timestamp;
            isReady = true;
        }

        public void Clear()
        {
            selectedAction = null;
            subAction = null;
            readyTimestamp = 0f;
            isReady = false;
            hasUsedSub = false;
        }

        public void InvalidateSlot(byte slotIndex)
        {
            if (selectedAction.HasValue && selectedAction.Value.SlotIndex == slotIndex)
                selectedAction = null;
            if (subAction.HasValue && subAction.Value.SlotIndex == slotIndex)
                subAction = null;
        }

        public void OnSlotRemoved(byte slotIndex)
        {
            if (selectedAction.HasValue)
            {
                var action = selectedAction.Value;
                if (action.SlotIndex == slotIndex)
                    selectedAction = null;
                else if (action.SlotIndex > slotIndex)
                {
                    action.SlotIndex--;
                    selectedAction = action;
                }
            }

            if (subAction.HasValue)
            {
                var action = subAction.Value;
                if (action.SlotIndex == slotIndex)
                    subAction = null;
                else if (action.SlotIndex > slotIndex)
                {
                    action.SlotIndex--;
                    subAction = action;
                }
            }
        }
    }

    public struct QueuedAction
    {
        public byte SlotIndex;
        public ItemDataSO ItemData;
        public byte TargetSeat;

        public QueuedAction(byte slotIndex, ItemDataSO itemData, byte targetSeat = ActionIntent.NoTarget)
        {
            SlotIndex = slotIndex;
            ItemData = itemData;
            TargetSeat = targetSeat;
        }
    }
}
