using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Marrow.Testing.Downloads;

// PORT-ONLY test fixture (SK-0): the test certificate. It is made in memory for each server and never written to a file or to any
// certificate store, so the machine does not trust it; the only party that does is the handler FakeDownloadTransport builds, which
// compares the presented certificate's bytes with the servers' own.
internal static class FakeTlsCertificates
{
    /// <summary>A self-signed ECDSA P-256 server certificate for the loopback names, valid for a day around now.</summary>
    internal static X509Certificate2 Create(string commonName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=" + commonName, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Parse("127.0.0.1"));
        names.AddIpAddress(IPAddress.Parse("127.0.0.2"));
        names.AddIpAddress(IPAddress.IPv6Loopback);
        names.AddDnsName("localhost");
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Re-loaded from its PKCS#12 bytes so the private key is usable by SslStream on every platform.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null);
    }
}
