using System.Threading.Tasks;

namespace AbsoluteZero.UI.Game.Bridge
{
    public interface ILocalPlayerCommands
    {
        bool TrySelectItem(byte slotIndex);
        void PressReady();
        Task LeaveMatchAsync();
    }
}
