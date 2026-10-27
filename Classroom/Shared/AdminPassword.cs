using System.Security.Cryptography;
using System.Text;
namespace Classroom;
static class AdminPassword
{
    static readonly byte[] Expected = Convert.FromBase64String("0PKIThfOs7RybmUBarWJo94RPyO45gxozV+jBCIGyP4=");
    public static bool Matches(string password) => CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(password)), Expected);
}
