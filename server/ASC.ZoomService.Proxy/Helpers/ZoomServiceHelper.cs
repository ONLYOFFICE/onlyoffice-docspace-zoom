using ASC.Web.Core.Files;
using ASC.ZoomService.Models;
using System.Text.Json;

namespace ASC.ZoomService.Proxy.Services
{
    [Scope]
    public class ZoomServiceHelper
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ZoomServiceHelper> _logger;

        private readonly string _jwtSecret;

        public ZoomServiceHelper(IConfiguration configuration, ILogger<ZoomServiceHelper> logger)
        {
            _configuration = configuration;
            _logger = logger;

            _jwtSecret = configuration["zoom:gate-secret"];
            _httpClient = new HttpClient();
        }

        public async Task<string> GetState(ZoomStateModel model, string region, string header, string zoomLinkCookie, bool forceAuth)
        {
            var domain = _configuration["zoom:zoom-domain"];

            var subdomain = GetSubdomainByRegionKey(region);

            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://{subdomain}.{domain}/zoomservice/zoom/state?noRedirect=true&forceAuth={forceAuth}" +
                $"&accountId={model.AccountId}&accountNumber={model.AccountNumber}&collaborationId={model.CollaborationId}");
            req.Headers.Add(ZoomAuthHandler.ZOOM_CONTEXT_HEADER, header);

            if (!string.IsNullOrEmpty(zoomLinkCookie))
            {
                req.Headers.Add("Cookie", $"ZoomLink={zoomLinkCookie}");
            }

            var response = await _httpClient.SendAsync(req);

            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<ZoomLinkResponse> GetLinks(ZoomLinkPayload model)
        {
            var regions = _configuration.GetSection("zoom:regions").Get<Dictionary<string, string>>();
            var domain = _configuration["zoom:zoom-domain"];

            var response = new ZoomLinkResponse()
            {
                Login = model.Login,
                TenantInfo = new()
            };

            foreach (var subdomain in regions.Values)
            {
                var zoomLink = await GetLink(model, $"{subdomain}.{domain}");
                if (zoomLink != null)
                {
                    response.TenantInfo.AddRange(zoomLink.TenantInfo);
                }
            }

            return response;
        }

        public async Task<string> PutLink(ZoomLinkPutPayload model, ZoomLinkResponse state, int chosenTenant, HttpResponse proxyResponse)
        {
            var domain = _configuration["zoom:zoom-domain"];

            var tenantInfo = state.TenantInfo.FirstOrDefault(t => t.Id == chosenTenant) ?? throw new Exception("incorrect chosen tenant");
            var subdomain = GetSubdomainByRegionKey(tenantInfo.Region);

            var response = await _httpClient.PutAsJsonAsync($"https://{subdomain}.{domain}/zoomservice/zoom/link", model);

            if (!response.IsSuccessStatusCode) return null;

            if (response.Headers.TryGetValues("ZoomLink", out var cookies) && cookies.Any())
            {
                proxyResponse.Cookies.Append("ZoomLink", cookies.First(), new CookieOptions() { Domain = domain, Expires = DateTimeOffset.Now.AddDays(30) });
            }

            return await response.Content.ReadAsStringAsync();
        }

        public async Task BroadcastDeauth(ZoomEventModel<ZoomDeauthorizationModel> zoomEvent)
        {
            var regions = _configuration.GetSection("zoom:regions").Get<Dictionary<string, string>>();
            var domain = _configuration["zoom:zoom-domain"];

            foreach (var subdomain in regions.Values)
            {
                await PostDeauth(zoomEvent, $"{subdomain}.{domain}");
            }
        }

        public async Task<string> PostHome(ZoomHomeModel zoomHomeModel, string header)
        {
            var domain = _configuration["zoom:zoom-domain"];

            var subdomain = GetSubdomainByRegionKey(_configuration["zoom:aws-region"]);

            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{subdomain}.{domain}/zoomservice/zoom/home");
            req.Headers.Add(ZoomAuthHandler.ZOOM_CONTEXT_HEADER, header);

            var response = await _httpClient.SendAsync(req);

            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync();
        }

        private async Task<ZoomLinkResponse> GetLink(ZoomLinkPayload model, string domain)
        {
            var jwtSecret = _configuration["zoom:gate-secret"];
            _logger.LogDebug($"Getting links from {domain}");
            var response = await _httpClient.PostAsJsonAsync($"https://{domain}/zoomservice/zoom/link", model);

            if (!response.IsSuccessStatusCode) return null;

            var jwt = await response.Content.ReadAsStringAsync();
            var body = JsonWebToken.Decode(jwt, jwtSecret);
            var zoomLink = JsonSerializer.Deserialize<ZoomLinkResponse>(body, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            return zoomLink;
        }

        private async Task PostDeauth(ZoomEventModel<ZoomDeauthorizationModel> zoomEvent, string domain)
        {
            var jwtSecret = _configuration["zoom:gate-secret"];

            _logger.LogDebug($"Posting deauth to {domain}");
            var response = await _httpClient.PostAsJsonAsync($"https://{domain}/zoomservice/zoom/deauth", zoomEvent);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Couldn't post deauth to {domain}. Status Code: {response.StatusCode}");
            }
        }

        private string GetSubdomainByRegionKey(string region)
        {
            var regions = _configuration.GetSection("zoom:regions").Get<Dictionary<string, string>>();
            if (!regions.TryGetValue(region, out var subdomain))
            {
                throw new Exception("incorrect region");
            }

            return subdomain;
        }
    }
}
