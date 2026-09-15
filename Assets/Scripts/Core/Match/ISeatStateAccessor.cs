using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Match
{
    public interface ISeatStateAccessor
    {
        float GetTemperature(byte seat);
        void SetTemperature(byte seat, float value);

        LifeState GetLifeState(byte seat);
        void SetLifeState(byte seat, LifeState value);

        float GetFanSpeed(byte seat);
        void SetFanSpeed(byte seat, float value);
        bool GetIsFanActive(byte seat);
        void SetIsFanActive(byte seat, bool value);
        bool GetIsFanUpgraded(byte seat);
        void SetIsFanUpgraded(byte seat, bool value);

        bool GetIsBasicBlocked(byte seat);
        void SetIsBasicBlocked(byte seat, bool value);

        PlayerModifiers GetModifiers(byte seat);
        void SetModifiers(byte seat, PlayerModifiers value);

        ItemSlotNetData[] GetInventorySlots(byte seat);
        void SetInventorySlots(byte seat, ItemSlotNetData[] slots);
        void ClearInventory(byte seat);

        bool IsConnected(byte seat);
        bool IsActive(byte seat);
    }
}
