// (c) Copyright Ascensio System SIA 2010-2022
//
// This program is a free software product.
// You can redistribute it and/or modify it under the terms
// of the GNU Affero General Public License (AGPL) version 3 as published by the Free Software
// Foundation. In accordance with Section 7(a) of the GNU AGPL its Section 15 shall be amended
// to the effect that Ascensio System SIA expressly excludes the warranty of non-infringement of
// any third-party rights.
//
// This program is distributed WITHOUT ANY WARRANTY, without even the implied warranty
// of MERCHANTABILITY or FITNESS FOR A PARTICULAR  PURPOSE. For details, see
// the GNU AGPL at: http://www.gnu.org/licenses/agpl-3.0.html
//
// You can contact Ascensio System SIA at Lubanas st. 125a-25, Riga, Latvia, EU, LV-1021.
//
// The  interactive user interfaces in modified source and object code versions of the Program must
// display Appropriate Legal Notices, as required under Section 5 of the GNU AGPL version 3.
//
// Pursuant to Section 7(b) of the License you must retain the original Product logo when
// distributing the program. Pursuant to Section 7(e) we decline to grant you any rights under
// trademark law for use of our trademarks.
//
// All the Product's GUI elements, including illustrations and icon sets, as well as technical writing
// content are licensed under the terms of the Creative Commons Attribution-ShareAlike 4.0
// International. See the License terms at http://creativecommons.org/licenses/by-sa/4.0/legalcode

using ASC.ApiSystem.Helpers;
using ASC.Web.Core.Files;
using ASC.ZoomService.Extensions;
using ASC.ZoomService.Models;
using ASC.ZoomService.Proxy.Services;
using Microsoft.Extensions.Caching.Distributed;
using System.Reflection;
using System.Text.Json;

namespace ASC.ZoomService.Proxy.Controllers;

[Scope]
[ApiController]
[Route("[controller]")]
public class ZoomController : ControllerBase
{
    private ILogger<ZoomController> Log { get; }
    private IConfiguration Configuration { get; }
    private ZoomAccountHelper ZoomAccountHelper { get; }
    private IDistributedCache Cache { get; }
    private ZoomServiceHelper ZoomServiceHelper { get; }

    public ZoomController(
        ILogger<ZoomController> log,
        IConfiguration configuration,
        ZoomAccountHelper zoomAccountHelper,
        IDistributedCache cache,
        ZoomServiceHelper zoomServiceHelper
        )
    {
        Log = log;
        Configuration = configuration;
        ZoomAccountHelper = zoomAccountHelper;
        Cache = cache;
        ZoomServiceHelper = zoomServiceHelper;
    }

    #region For TEST api

    [HttpGet("test")]
    public IActionResult Check()
    {
        return Ok(new
        {
            value = "Zoom proxy api works"
        });
    }

    [HttpGet("version")]
    public IActionResult Version()
    {
        return Ok(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
    }

    #endregion

    #region API methods

    [HttpGet("state")]
    [AllowCrossSiteJson]
    [Authorize(AuthenticationSchemes = ZoomAuthHandler.ZOOM_AUTH_SCHEME_HEADER)]
    public async Task<IActionResult> GetState([FromQuery] ZoomStateModel model, [FromQuery] bool noRedirect = false, [FromQuery] bool forceAuth = false)
    {
        var uid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_UID)?.Value;
        var mid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_MID)?.Value;
        model.TenantId = null;

        if (model.AccountId.Contains('_'))
        {
            // ToDo: we might have a collision
            model.AccountId = model.AccountId.Replace("_", "--");
        }

        var jwtSecret = Configuration["zoom:gate-secret"];
        string regionToProxy = null;
        string zoomLinkCookie = null;

        ZoomCollaborationCachedRoom collaboration = null;
        var collaborationIsActive = !string.IsNullOrWhiteSpace(model.CollaborationId) && !"none".Equals(model.CollaborationId);
        if (collaborationIsActive)
        {
            Log.LogDebug($"GetState(): Got CollaborationId, getting collaboration from cache");
            collaboration = Cache.GetCollaboration(mid);

            if (collaboration != null)
            {
                regionToProxy = collaboration.TenantRegion;
            }
        }
        else
        {
            if (Request.Cookies.TryGetValue("ZoomLink", out zoomLinkCookie))
            {
                try
                {
                    Log.LogDebug($"GetState(): ZoomLink Cookie is not null, getting confirm link using jwt");
                    var zoomLinkJson = JsonWebToken.Decode(zoomLinkCookie, jwtSecret);
                    var zoomLink = JsonSerializer.Deserialize<ZoomLinkCookie>(zoomLinkJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                    regionToProxy = zoomLink.TenantRegion;
                }
                catch (Exception ex)
                {
                    Log.LogWarning(ex, $"GetState(): ZoomLink jwt could not be parsed");
                    Response.Cookies.Delete("ZoomLink");
                }
            }
        }

        regionToProxy ??= Configuration["zoom:aws-region"];

        var link = await ZoomServiceHelper.GetState(model, regionToProxy, Request.Headers[ZoomAuthHandler.ZOOM_CONTEXT_HEADER], zoomLinkCookie, forceAuth);
        if (link == null) return BadRequest();
        return noRedirect ? Ok(link) : Redirect(link);
    }

    [HttpGet("install")]
    public IActionResult GetInstall([FromQuery] string state)
    {
        return Redirect($"https://zoom.us/oauth/authorize?response_type=code&client_id={ZoomAccountHelper.GetLoginProvider().ClientID}" +
            $"&redirect_uri={Configuration["zoom:zoom-redirect-uri"]}" +
            $"{(state != null ? $"&state={state}" : "")}");
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome([FromQuery] string code, [FromQuery] string state)
    {
        return BadRequest();
    }

    [HttpPost("link")]
    public async Task<IActionResult> PostLink(ZoomLinkPayload model)
    {
        var response = await ZoomServiceHelper.GetLinks(model);

        var jwtSecret = Configuration["zoom:gate-secret"];

        return Ok(JsonWebToken.Encode(response, jwtSecret));
    }

    [HttpPut("link")]
    public async Task<IActionResult> PutLink(ZoomLinkPutPayload model)
    {
        try
        {
            var jwtSecret = Configuration["zoom:gate-secret"];

            var stateJson = JsonWebToken.Decode(model.State, jwtSecret);

            var state = JsonSerializer.Deserialize<ZoomLinkResponse>(stateJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

            // jwt is valid, we can proxy

            var redirectLink = await ZoomServiceHelper.PutLink(model, state, model.ChosenTenant, Response);
            return Ok(redirectLink);
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, $"PutLink(): Error while linking");
            var response = new ZoomIntegrationPayload()
            {
                Home = Configuration["zoom:home"],
                Error = ex.Message,
            };
            return BadRequest(response);
        }
    }

    [HttpPost("home")]
    [AllowCrossSiteJson]
    public async Task<IActionResult> PostHome(ZoomHomeModel model)
    {
        return BadRequest();
    }

    [HttpPost("deauth")]
    [Authorize(AuthenticationSchemes = ZoomHookAuthHandler.ZOOM_HOOK_AUTH_SCHEME)]
    public async Task<IActionResult> DeauthorizationHook(ZoomEventModel<ZoomDeauthorizationModel> zoomEvent)
    {
        try
        {
            await ZoomServiceHelper.BroadcastDeauth(zoomEvent);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, $"DeauthorizationHook(): Error");
            return BadRequest();
        }
        return Ok();
    }

    #endregion

    #region private methods

    private class ZoomIntegrationPayload
    {
        public string ConfirmLink { get; set; }
        public string Error { get; set; }
        public string Home { get; set; } = "zoomservice";
        public string DocSpaceUrl { get; set; }
        public string OwnAccountId { get; set; }

        public ZoomCollaborationRoom Collaboration { get; set; }
    }

    private class ZoomAuthPayload
    {
        public string State { get; set; }
        public string Challenge { get; set; }
    }

    #endregion
}
