using System.Collections.Generic;
using UnityEngine;

namespace AbsoluteZero.Core.Session
{
    public class SessionParticipantTable
    {
        readonly Dictionary<string, SessionParticipantEntry> _byParticipantId = new();
        readonly Dictionary<ulong, SessionParticipantEntry> _byClientId = new();
        byte _nextSeatIndex;

        public int Count => _byParticipantId.Count;

        public SessionParticipantEntry Register(string participantId, string sessionToken)
        {
            if (string.IsNullOrEmpty(participantId) || string.IsNullOrEmpty(sessionToken))
            {
                Debug.LogError("[SessionParticipantTable] ParticipantId and SessionToken must not be empty");
                return null;
            }

            if (_byParticipantId.TryGetValue(participantId, out var existing))
            {
                Debug.LogWarning($"[SessionParticipantTable] Participant already registered: {participantId}");
                return existing;
            }

            byte seat = _nextSeatIndex++;
            var entry = new SessionParticipantEntry(participantId, sessionToken, seat);
            _byParticipantId[participantId] = entry;

            Debug.Log($"[SessionParticipantTable] Registered: {participantId} → Seat {seat}");
            return entry;
        }

        public bool TryValidateAndBind(string participantId, string token, ulong clientId, out SessionParticipantEntry entry)
        {
            entry = null;

            if (!_byParticipantId.TryGetValue(participantId, out entry))
            {
                Debug.LogWarning($"[SessionParticipantTable] Unknown participant: {participantId}");
                return false;
            }

            if (entry.SessionToken != token)
            {
                Debug.LogWarning($"[SessionParticipantTable] Token mismatch for {participantId}");
                entry = null;
                return false;
            }

            if (entry.IsConnected && entry.CurrentClientId.HasValue)
            {
                Debug.LogWarning($"[SessionParticipantTable] Duplicate connection for {participantId} (already client {entry.CurrentClientId.Value})");
                entry = null;
                return false;
            }

            if (_byClientId.TryGetValue(clientId, out var existingEntry))
            {
                Debug.LogWarning($"[SessionParticipantTable] ClientId {clientId} already bound to {existingEntry.ParticipantId}");
                entry = null;
                return false;
            }

            entry.IsConnected = true;
            entry.CurrentClientId = clientId;
            _byClientId[clientId] = entry;

            Debug.Log($"[SessionParticipantTable] Bound: {participantId} → ClientId {clientId} (Seat {entry.SeatIndex})");
            return true;
        }

        public void Unbind(ulong clientId)
        {
            if (!_byClientId.TryGetValue(clientId, out var entry)) return;

            entry.IsConnected = false;
            entry.CurrentClientId = null;
            _byClientId.Remove(clientId);

            Debug.Log($"[SessionParticipantTable] Unbound: {entry.ParticipantId} (ClientId {clientId}, Seat {entry.SeatIndex})");
        }

        public bool TryGetByParticipantId(string participantId, out SessionParticipantEntry entry)
        {
            return _byParticipantId.TryGetValue(participantId, out entry);
        }

        public bool TryGetByClientId(ulong clientId, out SessionParticipantEntry entry)
        {
            return _byClientId.TryGetValue(clientId, out entry);
        }

        public IEnumerable<SessionParticipantEntry> AllEntries => _byParticipantId.Values;

        public void Clear()
        {
            _byParticipantId.Clear();
            _byClientId.Clear();
            _nextSeatIndex = 0;
            Debug.Log("[SessionParticipantTable] Cleared");
        }
    }
}
