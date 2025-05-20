using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace ASC.ZoomService.Middlewares
{
    public static class ParseTenantMiddleware
    {
        public static async Task ParseMiddleware(HttpContext context, Func<Task> next)
        {
            await ParseTenant(context);
            await next();
        }

        public static async Task ParseTenant(HttpContext context)
        {
            var logger = context.RequestServices.GetService<ILogger<ParseTenantHubFilter>>();

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

        private static async Task<bool> TryParseFromDomain(string domain, HttpContext httpContent, TenantManager tenantManager, ILogger<ParseTenantHubFilter> logger)
        {
            domain = domain.Replace(".", @"\.");

            var regex = new Regex($@"http[s]{{0,1}}:\/\/([a-z\-0-9]+)\.{domain}");
            var uri = httpContent.Request.Url().AbsoluteUri;
            logger.LogDebug($"Current tenant is null, trying to find one from host {uri} using {domain} as base domain");

            Match match = regex.Match(uri);
            if (match.Success)
            {
                var tenantAlias = match.Groups[1].Value;

                var hostedSolution = httpContent.RequestServices.GetService<HostedSolution>();
                var tenant = await hostedSolution.GetTenantAsync(tenantAlias);
                logger.LogDebug($"Tenant alias is '{tenantAlias}', setting current tenant to {tenant.Id}");
                tenantManager.SetCurrentTenant(tenant);
                return true;
            }
            return false;
        }
    }

    public class ParseTenantHubFilter : IHubFilter
    {
        public async ValueTask<object> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object>> next)
        {
            await ParseTenantMiddleware.ParseTenant(context.Context.GetHttpContext());
            return await next(context);
        }
    }
}
