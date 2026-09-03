using ProtoLink.Communicator.Windows.Models;

namespace ProtoLink.Communicator.Windows.Services;

public interface ITokenService
{
    void SaveToken(TokenData data);
    TokenData? LoadToken();
    void ClearToken();
}
