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

        public static async Task ParseTenant(HttpContext context, TenantManager tenantManager = null)
        {
            var logger = context.RequestServices.GetService<ILogger<ParseTenant>>();

            try
            {
                tenantManager ??= context.RequestServices.GetService<TenantManager>();

                if (tenantManager.GetCurrentTenant(false) == null)
                {
                    var configuration = context.RequestServices.GetService<IConfiguration>();

                    logger.LogDebug($"Current tenant is null, trying to find one");
                    var tenant = await ParseFromDomain(configuration["zoom:zoom-domain"], context);
                    tenant ??= await ParseFromDomain(configuration["core:base-domain"], context);

                    if (tenant == null)
                    {
                        logger.LogWarning($"No tenant found");
                        return;
                    }

                    logger.LogDebug($"Setting current tenant to {tenant.Id}");
                    tenantManager.SetCurrentTenant(tenant);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Couldn't parse tenant");
            }
        }

        private static async Task<Tenant> ParseFromDomain(string domainIn, HttpContext httpContent)
        {
            var domain = domainIn.Replace(".", @"\.");

            var regex = new Regex($@"http[s]{{0,1}}:\/\/([a-z\-0-9]+)\.{domain}");
            var uri = httpContent.Request.Url().AbsoluteUri;

            Match match = regex.Match(uri);
            if (match.Success)
            {
                var tenantAlias = match.Groups[1].Value;

                var hostedSolution = httpContent.RequestServices.GetService<HostedSolution>();
                var tenant = await hostedSolution.GetTenantAsync(tenantAlias);
                return tenant;
            }
            return null;
        }
    }
}
