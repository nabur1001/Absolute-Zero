using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Combat
{
    public enum BarrierState : byte
    {
        Idle,
        Waiting,
        Completed,
        Canceled,
        TimedOut
    }

    public sealed class PresentationBarrier
    {
        readonly HashSet<ulong> _pendingClients = new();
        uint _currentSequence;

        public BarrierState State { get; private set; } = BarrierState.Idle;
        public bool IsActive => State == BarrierState.Waiting;
        public bool IsComplete => State == BarrierState.Completed;
        public uint CurrentSequence => _currentSequence;

        public void Begin(uint sequence, IEnumerable<ulong> expectedClientIds)
        {
            _currentSequence = sequence;
            _pendingClients.Clear();

            foreach (var id in expectedClientIds)
                _pendingClients.Add(id);

            if (_pendingClients.Count == 0)
            {
                State = BarrierState.Completed;
                Debug.Log($"[PresentationBarrier] Begin seq={sequence}, no clients — immediate complete");
                return;
            }

            State = BarrierState.Waiting;
            Debug.Log($"[PresentationBarrier] Begin seq={sequence}, expecting {_pendingClients.Count} clients");
        }

        public void ReceiveAck(uint sequence, ulong senderClientId)
        {
            if (State != BarrierState.Waiting)
            {
                Debug.Log($"[PresentationBarrier] ACK ignored: not waiting (state={State}, seq={sequence}, sender={senderClientId})");
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

            if (_pendingClients.Count == 0)
                State = BarrierState.Completed;
        }

        public void HandleDisconnect(ulong clientId)
        {
            if (State != BarrierState.Waiting) return;

            if (_pendingClients.Remove(clientId))
            {
                Debug.Log($"[PresentationBarrier] Disconnect: removed {clientId}, remaining={_pendingClients.Count}");
                if (_pendingClients.Count == 0)
                    State = BarrierState.Completed;
            }
        }

        public void Cancel(string reason)
        {
            if (State != BarrierState.Waiting) return;

            Debug.Log($"[PresentationBarrier] Cancelled: {reason} (seq={_currentSequence})");
            _pendingClients.Clear();
            State = BarrierState.Canceled;
        }

        public IEnumerator WaitForCompletion(float timeoutSeconds)
        {
            if (State == BarrierState.Completed)
                yield break;

            float elapsed = 0f;
            while (State == BarrierState.Waiting && elapsed < timeoutSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (State == BarrierState.Waiting)
            {
                Debug.LogWarning($"[PresentationBarrier] Timeout after {timeoutSeconds}s (seq={_currentSequence}, pending={_pendingClients.Count})");
                _pendingClients.Clear();
                State = BarrierState.TimedOut;
            }
        }

        public void Reset()
        {
            _pendingClients.Clear();
            State = BarrierState.Idle;
            _currentSequence = 0;
        }
    }
}
