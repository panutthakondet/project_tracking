namespace ProjectTracking.Services
{
    public static class ProfileImagePathResolver
    {
        public const string DefaultPath = "/images/Profile/profile.png";

        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DefaultPath;
            }

            var path = value.Trim().Replace('\\', '/');
            if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            if (path.StartsWith("~/", StringComparison.Ordinal))
            {
                path = path[1..];
            }

            if (path.StartsWith("wwwroot/", StringComparison.OrdinalIgnoreCase))
            {
                path = path["wwwroot".Length..];
            }

            return path.StartsWith("/", StringComparison.Ordinal)
                ? path
                : "/" + path.TrimStart('/');
        }
    }
}
