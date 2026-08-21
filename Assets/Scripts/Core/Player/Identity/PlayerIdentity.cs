using System;

namespace AbsoluteZero.Core.Player.Identity
{
    public readonly struct PlayerIdentity : IEquatable<PlayerIdentity>
    {
        public byte PlayerIndex { get; }
        public ulong ClientId { get; }

        public PlayerIdentity(byte playerIndex, ulong clientId)
        {
            PlayerIndex = playerIndex;
            ClientId = clientId;
        }

        public bool Equals(PlayerIdentity other)
            => PlayerIndex == other.PlayerIndex && ClientId == other.ClientId;

        public override bool Equals(object obj)
            => obj is PlayerIdentity other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(PlayerIndex, ClientId);

        public override string ToString()
            => $"P{PlayerIndex}@{ClientId}";

        public static bool operator ==(PlayerIdentity a, PlayerIdentity b) => a.Equals(b);
        public static bool operator !=(PlayerIdentity a, PlayerIdentity b) => !a.Equals(b);
    }
}
