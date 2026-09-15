namespace AbsoluteZero.Core.Network
{
    public interface IDisconnectHandler
    {
        void OnPlayerDisconnected(ulong clientId);
    }
}
