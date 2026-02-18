using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ProjectTracking.Helpers
{
    public static class SecurityHelper
    {
        // ------------------------------
        // Legacy helper (ใช้กับ token และ password แบบเดิม)
        // ------------------------------
        // SHA-256 -> hex lowercase 64 chars
        public static string Sha256(string input)
        {
            input = (input ?? "").Trim();
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        // token แบบสุ่ม (ส่งไปทาง email) แล้วเอาไป hash เก็บใน DB
        public static string GenerateToken(int bytes = 32)
        {
            var data = RandomNumberGenerator.GetBytes(bytes);
            return Convert.ToBase64String(data)
                .Replace("+", "-")
                .Replace("/", "_")
                .TrimEnd('=');
        }

        // ------------------------------
        // ✅ Password hashing (PBKDF2) + Backward compatible with legacy SHA256
        // Format: PBKDF2$<iter>$<salt_b64>$<hash_b64>
        // ------------------------------
        private const int Pbkdf2Iterations = 100_000;
        private const int Pbkdf2SaltBytes = 16;
        private const int Pbkdf2KeyBytes = 32;
        private static readonly Regex LegacySha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

        public static string HashPassword(string password)
        {
            password = (password ?? "").Trim();
            var salt = RandomNumberGenerator.GetBytes(Pbkdf2SaltBytes);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var key = pbkdf2.GetBytes(Pbkdf2KeyBytes);
            return $"PBKDF2${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        public static bool VerifyPassword(string password, string storedHash)
        {
            password = (password ?? "").Trim();
            storedHash = (storedHash ?? "").Trim();

            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            // ✅ Legacy: SHA256 hex
            if (LegacySha256Hex.IsMatch(storedHash))
            {
                var legacy = Sha256(password);
                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(legacy),
                    Encoding.UTF8.GetBytes(storedHash.ToLowerInvariant())
                );
            }

            // ✅ New: PBKDF2$iter$salt$hash
            var parts = storedHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || !parts[0].Equals("PBKDF2", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!int.TryParse(parts[1], out var iter) || iter < 10_000)
                return false;

            byte[] salt;
            byte[] expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch
            {
                return false;
            }

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iter, HashAlgorithmName.SHA256);
            var actual = pbkdf2.GetBytes(expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        public static bool IsLegacyPasswordHash(string storedHash)
        {
            storedHash = (storedHash ?? "").Trim();
            return LegacySha256Hex.IsMatch(storedHash);
        }
    }
}