using System.Threading.Tasks;

namespace AbsoluteZero.UI.Game.Bridge
{
    public interface ILocalPlayerCommands
    {
        bool TrySelectItem(byte slotIndex);
        bool TrySelectItemWithTarget(byte slotIndex, byte targetSeat);
        bool TryCancelSelection();
        void PressReady();
        void UseGhostSkill(byte skillIndex, byte targetSeat);
        Task LeaveMatchAsync();
        void SubmitRematchDecision(bool accept, uint voteEpoch);
    }
}
