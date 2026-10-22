using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Classroom;
static class ClassCertificate
{
    public static X509Certificate2 Create(string name = "Classroom", bool persist = false)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temporary = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(5));
        // Schannel on Windows needs an imported key, rather than an ephemeral RSA key.
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var pfx = temporary.Export(X509ContentType.Pfx, password);
        try { return X509CertificateLoader.LoadPkcs12(pfx, password, persist ? X509KeyStorageFlags.PersistKeySet : X509KeyStorageFlags.DefaultKeySet); }
        finally { CryptographicOperations.ZeroMemory(pfx); }
    }
    public static X509Certificate2 LoadIdentity(string role)
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        var subject = "CN=Classroom." + role;
        var existing = store.Certificates.Cast<X509Certificate2>().FirstOrDefault(c => c.Subject == subject && c.HasPrivateKey && c.NotAfter > DateTime.Now.AddDays(1));
        if (existing != null) return existing;
        var certificate = Create("Classroom." + role, true);
        store.Add(certificate);
        return certificate;
    }
}
