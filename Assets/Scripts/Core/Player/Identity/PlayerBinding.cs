using Unity.Netcode;

namespace AbsoluteZero.Core.Player.Identity
{
    public sealed class PlayerBinding
    {
        public PlayerIdentity Identity { get; private set; }
        public PlayerState State { get; }
        public PlayerInventory Inventory { get; }
        public NetworkObject NetworkObject { get; }

        public bool IsValid => State != null
                               && NetworkObject != null
                               && NetworkObject.IsSpawned;

        public PlayerBinding(PlayerState state, PlayerInventory inventory,
                             NetworkObject networkObject)
        {
            State = state;
            Inventory = inventory;
            NetworkObject = networkObject;
        }

        internal void AssignIdentity(PlayerIdentity identity)
        {
            Identity = identity;
        }
    }
}
