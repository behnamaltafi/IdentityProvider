using System.Security.Cryptography;
using System.Text;

namespace IdentityProvider.Infrastructure.Identity;

// Publish a one-way session version; the ASP.NET Identity security stamp stays private.
public static class SessionVersion
{
    public static string Create(string securityStamp, string secret) => Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(securityStamp ?? "")));
}
