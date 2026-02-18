using System.Security.Cryptography;
using System.Text;

namespace ProjectTracking.Helpers
{
    public static class Sha256Helper
    {
        public static string Hash(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);

            var builder = new StringBuilder();
            foreach (var b in hash)
                builder.Append(b.ToString("x2")); // hex 64 chars

            return builder.ToString();
        }
    }
}