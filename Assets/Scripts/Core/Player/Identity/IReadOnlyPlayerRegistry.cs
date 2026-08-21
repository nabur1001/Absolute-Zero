using System;
using System.Collections.Generic;

namespace AbsoluteZero.Core.Player.Identity
{
    public interface IReadOnlyPlayerRegistry
    {
        IReadOnlyCollection<PlayerBinding> Players { get; }
        int ReadyCount { get; }
        bool TryGetByPlayerIndex(byte index, out PlayerBinding player);
        bool TryGetByClientId(ulong clientId, out PlayerBinding player);
        event Action<PlayerBinding> Registered;
        event Action<PlayerIdentity> Unregistered;
    }
}
