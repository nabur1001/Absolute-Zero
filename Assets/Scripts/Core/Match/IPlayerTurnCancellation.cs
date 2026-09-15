namespace AbsoluteZero.Core.Match
{
    public interface IPlayerTurnCancellation
    {
        void CancelTurnParticipation(byte seat);
        void ClearPendingIntent(byte seat);
    }
}
