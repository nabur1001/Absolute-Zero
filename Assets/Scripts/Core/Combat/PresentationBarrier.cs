using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Combat
{
    public sealed class PresentationBarrier
    {
        readonly HashSet<ulong> _pendingClients = new();
        uint _currentSequence;
        bool _isActive;

        public bool IsActive => _isActive;
        public bool IsComplete => _isActive && _pendingClients.Count == 0;
        public uint CurrentSequence => _currentSequence;

        public void Begin(uint sequence, IEnumerable<ulong> expectedClientIds)
        {
            _currentSequence = sequence;
            _pendingClients.Clear();
            _isActive = true;

            foreach (var id in expectedClientIds)
                _pendingClients.Add(id);

            Debug.Log($"[PresentationBarrier] Begin seq={sequence}, expecting {_pendingClients.Count} clients");
        }

        public void ReceiveAck(uint sequence, ulong senderClientId)
        {
            if (!_isActive)
            {
                Debug.Log($"[PresentationBarrier] ACK ignored: no active barrier (seq={sequence}, sender={senderClientId})");
                return;
            }

            if (sequence != _currentSequence)
            {
                Debug.Log($"[PresentationBarrier] ACK ignored: seq mismatch (got={sequence}, current={_currentSequence}, sender={senderClientId})");
                return;
            }

            if (!_pendingClients.Remove(senderClientId))
            {
                Debug.Log($"[PresentationBarrier] ACK ignored: sender {senderClientId} not in pending (seq={sequence})");
                return;
            }

            Debug.Log($"[PresentationBarrier] ACK received: sender={senderClientId}, remaining={_pendingClients.Count}");
        }

        public void HandleDisconnect(ulong clientId)
        {
            if (!_isActive) return;

            if (_pendingClients.Remove(clientId))
                Debug.Log($"[PresentationBarrier] Disconnect: removed {clientId}, remaining={_pendingClients.Count}");
        }

        public void Cancel(string reason)
        {
            if (!_isActive) return;

            Debug.Log($"[PresentationBarrier] Cancelled: {reason} (seq={_currentSequence})");
            _pendingClients.Clear();
            _isActive = false;
        }

        public IEnumerator WaitForCompletion(float timeoutSeconds)
        {
            if (IsComplete)
            {
                _isActive = false;
                yield break;
            }

            float elapsed = 0f;
            while (!IsComplete && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!IsComplete)
            {
                Debug.LogWarning($"[PresentationBarrier] Timeout after {timeoutSeconds}s (seq={_currentSequence}, pending={_pendingClients.Count})");
                _pendingClients.Clear();
            }

            _isActive = false;
        }

        public void Reset()
        {
            _pendingClients.Clear();
            _isActive = false;
            _currentSequence = 0;
        }
    }
}
