using AbsoluteZero.Core.Buff;
using AbsoluteZero.Core.Combat;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;
using AbsoluteZero.Core.Player;

namespace AbsoluteZero.Core.Turn
{
    public interface ITurnContext
    {
        TurnPhase Phase { get; }
        bool CanAcceptEmotes { get; }
        double PrepStartTime { get; }
        float PrepDurationSeconds { get; }

        PlayerState GetPlayer(int index);
        PlayerModifiers[] GetModifiers();
        TemperatureSystem GetTempSystem();
        BuffDebuffSystem GetBuffSystem();
        ItemDropTable GetDropTable();

        void ReceivePresentationAck(uint sequence, ulong senderClientId);
        void PublishItemUsed(byte playerIdx, byte slotIdx, byte category, bool isSub);
        void PublishOpponentRevealed(byte playerIdx, short itemId);
    }
}
