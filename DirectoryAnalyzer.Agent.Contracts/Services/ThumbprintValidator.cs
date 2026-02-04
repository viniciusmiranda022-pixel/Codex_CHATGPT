using System;
using System.Linq;

namespace DirectoryAnalyzer.Agent.Contracts.Services
{
    public static class ThumbprintValidator
    {
        public static bool IsValid(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return false;
            }

            var normalized = new string(thumbprint.Where(char.IsLetterOrDigit).ToArray());
            if (normalized.Length < 40)
            {
                return false;
            }

            return normalized.All(IsHexChar);
        }

        private static bool IsHexChar(char value)
        {
            return (value >= '0' && value <= '9')
                || (value >= 'a' && value <= 'f')
                || (value >= 'A' && value <= 'F');
        }
    }
}
