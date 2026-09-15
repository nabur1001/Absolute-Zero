using System;
using AbsoluteZero.Core.Common;
using AbsoluteZero.Core.Item;

namespace AbsoluteZero.UI.Game.Bridge
{
    public interface IGameDataBridge
    {
        int SeatCount { get; }
        byte LocalSeatIndex { get; }
        bool TryGetSeat(byte seat, out SeatSnapshot snapshot);
        MatchSnapshot CurrentMatch { get; }

        event Action<byte, SeatSnapshot> OnSeatSnapshotChanged;
        event Action<MatchSnapshot> OnMatchSnapshotChanged;

        event Action<byte, SeatSnapshot> OnSeatRegistered;
        event Action<byte> OnSeatUnregistered;

        event Action<TurnPhase, TurnPhase> OnPhaseChanged;
        event Action<EnvironmentType> OnEnvironmentAnnounced;
        event Action<byte, float> OnTempOverride;
        event Action OnTempOverridesClear;
        event Action<bool> OnLocalHasSelectedItemChanged;
        event Action<byte, short> OnOpponentRevealed;

        event Action<int> OnCurrentAttackerChanged;
        event Action<MatchSnapshot> OnRoundResult;
        event Action<MatchSnapshot> OnMatchEnd;
        event Action<byte> OnRematchDecisionChanged;
    }
}
