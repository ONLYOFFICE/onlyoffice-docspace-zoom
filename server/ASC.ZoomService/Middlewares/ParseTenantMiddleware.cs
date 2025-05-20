using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace ASC.ZoomService.Middlewares
{
    public class ParseTenant { }
    public static class ParseTenantMiddleware
    {
        public static async Task ParseMiddleware(HttpContext context, Func<Task> next)
        {
            await ParseTenant(context);
            await next();
        }

        public static async Task ParseTenant(HttpContext context)
        {
            var logger = context.RequestServices.GetService<ILogger<ParseTenant>>();

            try
            {
                var tenantManager = context.RequestServices.GetService<TenantManager>();

                if (tenantManager.GetCurrentTenant(false) == null)
                {
                    var configuration = context.RequestServices.GetService<IConfiguration>();

                    if (!await TryParseFromDomain(configuration["zoom:zoom-domain"], context, tenantManager, logger))
                    {
                        await TryParseFromDomain(configuration["core:base-domain"], context, tenantManager, logger);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't parse tenant");
            }
        }

        private static async Task<bool> TryParseFromDomain(string domainIn, HttpContext httpContent, TenantManager tenantManager, ILogger<ParseTenant> logger)
        {
            var domain = domainIn.Replace(".", @"\.");

            var regex = new Regex($@"http[s]{{0,1}}:\/\/([a-z\-0-9]+)\.{domain}");
            var uri = httpContent.Request.Url().AbsoluteUri;
            logger.LogDebug($"Current tenant is null, trying to find one from uri {uri} using {domainIn} as base domain");

            Match match = regex.Match(uri);
            if (match.Success)
            {
                var tenantAlias = match.Groups[1].Value;
                logger.LogDebug($"Found tenant alias {tenantAlias}");

                var hostedSolution = httpContent.RequestServices.GetService<HostedSolution>();
                var tenant = await hostedSolution.GetTenantAsync(tenantAlias);
                if (tenant == null)
                {
                    logger.LogWarning($"No tenant found with alias '{tenantAlias}'");
                    return false;
                }
                logger.LogDebug($"Tenant alias is '{tenantAlias}', setting current tenant to {tenant.Id}");
                tenantManager.SetCurrentTenant(tenant);
                return true;
            }
            logger.LogDebug("No regex match found");
            return false;
        }
    }
}
