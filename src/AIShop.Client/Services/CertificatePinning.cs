using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AIShop.Client.Services
{
    public static class CertificatePinning
    {
        private static string _host;
        private static string _sha256;
        private static bool _configured;

        public static void Configure(string baseUrl, string pinnedSha256)
        {
            if (_configured || string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(pinnedSha256))
            {
                return;
            }

            var uri = new Uri(baseUrl);
            _host = uri.Host;
            _sha256 = Normalize(pinnedSha256);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.ServerCertificateValidationCallback += Validate;
            _configured = true;
        }

        private static bool Validate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                return true;
            }

            var request = sender as HttpWebRequest;
            if (request == null || !string.Equals(request.RequestUri.Host, _host, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (certificate == null)
            {
                return false;
            }

            using (var cert = new X509Certificate2(certificate))
            using (var sha = SHA256.Create())
            {
                var actual = BitConverter.ToString(sha.ComputeHash(cert.RawData)).Replace("-", "").ToLowerInvariant();
                return string.Equals(actual, _sha256, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string Normalize(string value)
        {
            return new string(value.Where(Uri.IsHexDigit).ToArray()).ToLowerInvariant();
        }
    }
}
