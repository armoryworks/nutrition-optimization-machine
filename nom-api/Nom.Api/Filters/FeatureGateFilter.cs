using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;

namespace Nom.Api.Filters
{
    /// <summary>
    /// Hides a controller behind a configuration switch (Features:&lt;name&gt;=true).
    /// Used to quarantine endpoint layers that are not yet production-quality
    /// without deleting them: callers get 404 rather than fabricated data.
    /// </summary>
    public sealed class FeatureGateAttribute : ActionFilterAttribute
    {
        private readonly string _feature;
        public FeatureGateAttribute(string feature) => _feature = feature;

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var config = context.HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfiguration;
            var enabled = config?.GetValue<bool>($"Features:{_feature}") ?? false;
            if (!enabled)
            {
                context.Result = new NotFoundObjectResult(new { message = $"This capability ({_feature}) is not enabled on this server." });
                return;
            }
            base.OnActionExecuting(context);
        }
    }
}
