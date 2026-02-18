using Microsoft.AspNetCore.Builder;

namespace ProjectTracking.Middleware
{
    public static class RequireLoginMiddlewareExtensions
    {
        /// <summary>
        /// ใช้เรียก RequireLoginMiddleware แบบสั้น ๆ
        /// </summary>
        public static IApplicationBuilder UseRequireLogin(this IApplicationBuilder app)
        {
            return app.UseMiddleware<RequireLoginMiddleware>();
        }
    }
}