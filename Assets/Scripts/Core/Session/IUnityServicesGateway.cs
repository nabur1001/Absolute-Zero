using System.Threading.Tasks;

namespace AbsoluteZero.Core.Session
{
    public interface IUnityServicesGateway
    {
        bool IsInitialized { get; }
        bool IsSignedIn { get; }
        string PlayerId { get; }
        Task<Result<Unit>> InitializeAndSignInAsync(string profileOverride = null);
    }
}
