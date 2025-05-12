using System.Text.RegularExpressions;

namespace ASC.ZoomService.Middlewares
{
    public static class ParseTenantMiddleware
    {
        public static async Task ParseMiddleware(HttpContext context, Func<Task> next)
        {
            var tenantManager = context.RequestServices.GetService<TenantManager>();

            if (tenantManager.GetCurrentTenant(false) == null)
            {
                var configuration = context.RequestServices.GetService<IConfiguration>();
                var domain = configuration["zoom:zoom-domain"];
                domain = domain.Replace(".", @"\.");

                var regex = new Regex($@"http[s]{{0,1}}:\/\/([a-z\-0-9]+)\.{domain}");
                var uri = context.Request.Url().AbsoluteUri;

                Match match = regex.Match(uri);
                if (match.Success)
                {
                    var tenantAlias = match.Groups[1].Value;

                    var hostedSolution = context.RequestServices.GetService<HostedSolution>();
                    var tenant = await hostedSolution.GetTenantAsync(tenantAlias);
                    tenantManager.SetCurrentTenant(tenant);
                }
            }

            await next();
        } 
    }
}
