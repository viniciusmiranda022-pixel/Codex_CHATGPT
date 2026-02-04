using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace DirectoryAnalyzer.AgentContracts
{
    [DataContract]
    public sealed class AgentRequest
    {
        [DataMember(Order = 1)]
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        [DataMember(Order = 2)]
        public string ActionName { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        [DataMember(Order = 4)]
        public long TimestampUnixSeconds { get; set; }

        [DataMember(Order = 5)]
        public string Nonce { get; set; } = string.Empty;

        [DataMember(Order = 6)]
        public string Signature { get; set; } = string.Empty;
    }

    [DataContract]
    [KnownType(typeof(GetUsersResult))]
    public sealed class AgentResponse
    {
        [DataMember(Order = 1)]
        public string RequestId { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Status { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public long DurationMs { get; set; }

        [DataMember(Order = 4)]
        public object Payload { get; set; }

        [DataMember(Order = 5, EmitDefaultValue = false)]
        public AgentError Error { get; set; }

        public static AgentResponse Success(string requestId, long durationMs, object payload)
        {
            return new AgentResponse
            {
                RequestId = requestId,
                Status = "Success",
                DurationMs = durationMs,
                Payload = payload
            };
        }

        public static AgentResponse Failed(string requestId, string code, string message)
        {
            return new AgentResponse
            {
                RequestId = requestId,
                Status = "Failed",
                Error = new AgentError
                {
                    Code = code,
                    Message = message
                }
            };
        }

        public static AgentResponse FromException(string requestId, Exception ex)
        {
            return new AgentResponse
            {
                RequestId = requestId,
                Status = "Failed",
                Error = new AgentError
                {
                    Code = "UnhandledException",
                    Message = ex.Message,
                    Details = ex.StackTrace
                }
            };
        }
    }

    [DataContract]
    public sealed class AgentError
    {
        [DataMember(Order = 1)]
        public string Code { get; set; } = string.Empty;

        [DataMember(Order = 2)]
        public string Message { get; set; } = string.Empty;

        [DataMember(Order = 3, EmitDefaultValue = false)]
        public string Details { get; set; }
    }

    [DataContract]
    public sealed class GetUsersResult
    {
        [DataMember(Order = 1)]
        public List<UserRecord> Users { get; set; } = new List<UserRecord>();
    }

    [DataContract]
    public sealed class UserRecord
    {
        [DataMember(Order = 1)]
        public string SamAccountName { get; set; }

        [DataMember(Order = 2)]
        public string DisplayName { get; set; }

        [DataMember(Order = 3)]
        public bool Enabled { get; set; }
    }

    public static class AgentRequestSigner
    {
        public static string Sign(AgentRequest request, X509Certificate2 certificate)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (certificate == null)
            {
                throw new ArgumentNullException(nameof(certificate));
            }

            var payload = Encoding.UTF8.GetBytes(BuildPayload(request));
            var rsa = certificate.GetRSAPrivateKey();
            if (rsa != null)
            {
                var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return Convert.ToBase64String(signature);
            }

            var ecdsa = certificate.GetECDsaPrivateKey();
            if (ecdsa != null)
            {
                var signature = ecdsa.SignData(payload, HashAlgorithmName.SHA256);
                return Convert.ToBase64String(signature);
            }

            throw new InvalidOperationException("Certificate does not have a supported private key.");
        }

        public static bool VerifySignature(AgentRequest request, X509Certificate2 certificate)
        {
            if (request == null || certificate == null || string.IsNullOrWhiteSpace(request.Signature))
            {
                return false;
            }

            var payload = Encoding.UTF8.GetBytes(BuildPayload(request));
            byte[] signature;
            try
            {
                signature = Convert.FromBase64String(request.Signature);
            }
            catch (FormatException)
            {
                return false;
            }
            var rsa = certificate.GetRSAPublicKey();
            if (rsa != null)
            {
                return rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }

            var ecdsa = certificate.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                return ecdsa.VerifyData(payload, signature, HashAlgorithmName.SHA256);
            }

            return false;
        }

        private static string BuildPayload(AgentRequest request)
        {
            var parameters = request.Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var parameterString = string.Join("&", parameters
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}={entry.Value}"));

            return string.Join("|", new[]
            {
                request.RequestId ?? string.Empty,
                request.ActionName ?? string.Empty,
                request.TimestampUnixSeconds.ToString(),
                request.Nonce ?? string.Empty,
                parameterString
            });
        }
    }
}
