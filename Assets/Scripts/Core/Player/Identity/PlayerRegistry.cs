using System;
using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Player.Identity
{
    public sealed class PlayerRegistry : IReadOnlyPlayerRegistry
    {
        readonly Dictionary<ulong, PlayerBinding> _byClientId = new();
        readonly Dictionary<byte, PlayerBinding> _byIndex = new();
        readonly Dictionary<ulong, PlayerBinding> _pending = new();
        readonly List<PlayerBinding> _readyList = new();

        public IReadOnlyCollection<PlayerBinding> Players => _readyList;
        public int ReadyCount => _readyList.Count;
        public int PendingCount => _pending.Count;
        public int TotalCount => _pending.Count + _readyList.Count;

        public event Action<PlayerBinding> Registered;
        public event Action<PlayerIdentity> Unregistered;

        public bool TryGetByPlayerIndex(byte index, out PlayerBinding player)
        {
            if (_byIndex.TryGetValue(index, out player) && player.IsValid)
                return true;
            player = null;
            return false;
        }

        public bool TryGetByClientId(ulong clientId, out PlayerBinding player)
        {
            if (_byClientId.TryGetValue(clientId, out player) && player.IsValid)
                return true;
            player = null;
            return false;
        }

        public IEnumerable<PlayerBinding> EnumeratePending() => _pending.Values;

        public void RegisterPending(ulong clientId, PlayerBinding binding)
        {
            if (_pending.ContainsKey(clientId) || _byClientId.ContainsKey(clientId))
            {
                Debug.LogWarning($"[PlayerRegistry] RegisterPending: ClientId {clientId} already tracked — ignoring duplicate");
                return;
            }

            _pending[clientId] = binding;
            Debug.Log($"[PlayerRegistry] Pending registered: ClientId={clientId}");
        }

        public void PromoteToReady(ulong clientId, byte playerIndex)
        {
            if (!_pending.TryGetValue(clientId, out var binding))
            {
                Debug.LogWarning($"[PlayerRegistry] PromoteToReady: ClientId {clientId} not in pending");
                return;
            }

            if (_byIndex.ContainsKey(playerIndex))
            {
                Debug.LogError($"[PlayerRegistry] PromoteToReady: PlayerIndex {playerIndex} already assigned — blocking duplicate");
                return;
            }

            _pending.Remove(clientId);

            var identity = new PlayerIdentity(playerIndex, clientId);
            binding.AssignIdentity(identity);

            _byClientId[clientId] = binding;
            _byIndex[playerIndex] = binding;
            _readyList.Add(binding);

            Debug.Log($"[PlayerRegistry] Ready: {identity}");
            Registered?.Invoke(binding);
        }

        public void Unregister(PlayerIdentity identity)
        {
            bool removed = false;

            if (_byIndex.TryGetValue(identity.PlayerIndex, out var existing)
                && existing.Identity.ClientId == identity.ClientId)
            {
                _byIndex.Remove(identity.PlayerIndex);
                _byClientId.Remove(identity.ClientId);
                _readyList.Remove(existing);
                removed = true;
            }

            _pending.Remove(identity.ClientId);

            if (removed)
            {
                Debug.Log($"[PlayerRegistry] Unregistered: {identity}");
                Unregistered?.Invoke(identity);
            }
        }

        public void UnregisterByClientId(ulong clientId)
        {
            if (_byClientId.TryGetValue(clientId, out var binding))
            {
                Unregister(binding.Identity);
                return;
            }

            if (_pending.Remove(clientId))
                Debug.Log($"[PlayerRegistry] Removed pending ClientId={clientId}");
        }

        public void Clear()
        {
            _pending.Clear();
            _byClientId.Clear();
            _byIndex.Clear();
            _readyList.Clear();
            Debug.Log("[PlayerRegistry] Cleared");
        }
    }
}
