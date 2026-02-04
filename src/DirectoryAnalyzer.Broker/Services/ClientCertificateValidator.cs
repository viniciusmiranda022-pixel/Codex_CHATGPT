using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace DirectoryAnalyzer.Broker.Services
{
    public sealed class ClientCertificateValidator
    {
        private readonly HashSet<string> _allowedThumbprints;
        private readonly HashSet<string> _trustedCaThumbprints;

        public ClientCertificateValidator(IEnumerable<string> allowedThumbprints, IEnumerable<string> trustedCaThumbprints)
        {
            _allowedThumbprints = Normalize(allowedThumbprints);
            _trustedCaThumbprints = Normalize(trustedCaThumbprints);
        }

        public bool Validate(X509Certificate2 certificate)
        {
            if (certificate == null)
            {
                return false;
            }

            if (_allowedThumbprints.Count > 0)
            {
                return _allowedThumbprints.Contains(Normalize(certificate.Thumbprint));
            }

            if (_trustedCaThumbprints.Count > 0)
            {
                using (var chain = new X509Chain())
                {
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
                    chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

                    if (!chain.Build(certificate))
                    {
                        return false;
                    }

                    return chain.ChainElements
                        .Cast<X509ChainElement>()
                        .Any(element => _trustedCaThumbprints.Contains(Normalize(element.Certificate.Thumbprint)));
                }
            }

            return false;
        }

        private static HashSet<string> Normalize(IEnumerable<string> thumbprints)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (thumbprints == null)
            {
                return set;
            }

            foreach (var thumbprint in thumbprints)
            {
                var normalized = Normalize(thumbprint);
                if (!string.IsNullOrWhiteSpace(normalized))
                {
                    set.Add(normalized);
                }
            }

            return set;
        }

        private static string Normalize(string thumbprint)
        {
            return thumbprint?.Replace(" ", string.Empty).ToUpperInvariant();
        }
    }
}
