using Idara.API.Models;

namespace Idara.API.Services
{
    public interface IJwtService
    {
        string GenerateToken(User user);
    }
}
