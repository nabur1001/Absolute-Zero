namespace AbsoluteZero.Core.Session
{
    public class SessionParticipantEntry
    {
        public string ParticipantId { get; }
        public string SessionToken { get; }
        public byte SeatIndex { get; }
        public bool IsConnected { get; set; }
        public ulong? CurrentClientId { get; set; }

        public SessionParticipantEntry(string participantId, string sessionToken, byte seatIndex)
        {
            ParticipantId = participantId;
            SessionToken = sessionToken;
            SeatIndex = seatIndex;
            IsConnected = false;
            CurrentClientId = null;
        }
    }
}
