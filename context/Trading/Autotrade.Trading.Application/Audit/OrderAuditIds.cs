using System.Security.Cryptography;
using System.Text;

namespace Autotrade.Trading.Application.Audit;

public static class OrderAuditIds
{
    public static Guid ForClientOrderId(string clientOrderId)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            return Guid.NewGuid();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(clientOrderId));
        return new Guid(hash.AsSpan(0, 16));
    }
}
