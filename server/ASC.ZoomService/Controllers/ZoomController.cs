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
using ASC.Core.Common.Quota;
using ASC.Core.Common.Quota.Features;
using ASC.FederatedLogin;
using ASC.FederatedLogin.Helpers;
using ASC.FederatedLogin.LoginProviders;
using ASC.FederatedLogin.Profile;
using ASC.Files.Core.ApiModels.ResponseDto;
using ASC.Files.Core.VirtualRooms;
using ASC.Web.Api.Core;
using ASC.Web.Core.Files;
using ASC.Web.Files.Classes;
using ASC.Web.Files.Services.WCFService;
using ASC.Web.Files.Utils;
using ASC.Web.Studio.Core.Notify;
using ASC.ZoomService.Extensions;
using ASC.ZoomService.Helpers;
using ASC.ZoomService.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ASC.ApiSystem.Controllers;

[Scope]
[ApiController]
[Route("[controller]")]
public class ZoomController : ControllerBase
{
    private CommonMethods CommonMethods { get; }
    private HostedSolution HostedSolution { get; }
    private ILogger<ZoomController> Log { get; }

    private ApiSystemHelper ApiSystemHelper { get; }
    private SecurityContext SecurityContext { get; }
    private IConfiguration Configuration { get; }
    private TenantManager TenantManager { get; }
    private CoreSettings CoreSettings { get; }
    private ZoomAccountHelper ZoomAccountHelper { get; }
    private ZoomMultiTenantHelper ZoomMultiTenantHelper { get; }
    private TimeZonesProvider TimeZonesProvider { get; }
    private TimeZoneConverter TimeZoneConverter { get; }
    private UserManager UserManager { get; }
    private AccountLinker AccountLinker { get; }
    private UserManagerWrapper UserManagerWrapper { get; }
    private RequestHelper RequestHelper { get; }
    private FileUploader FileUploader { get; }
    private SocketManager SocketManager { get; }
    private FileDtoHelper FileDtoHelper { get; }
    private GlobalFolderHelper GlobalFolderHelper { get; }
    private IDistributedCache Cache { get; }
    private CspSettingsHelper CspSettingsHelper { get; }
    private TenantQuotaFeatureCheckerCount<CountPaidUserFeature> CountPaidUserChecker { get; }
    private StudioNotifyService StudioNotifyService { get; }

    public ZoomController(
        CommonMethods commonMethods,
        HostedSolution hostedSolution,
        ILogger<ZoomController> log,
        ApiSystemHelper apiSystemHelper,
        SecurityContext securityContext,
        IConfiguration configuration,
        TenantManager tenantManager,
        CoreSettings coreSettings,
        ZoomAccountHelper zoomAccountHelper,
        ZoomMultiTenantHelper zoomMultiTenantHelper,
        TimeZonesProvider timeZonesProvider,
        TimeZoneConverter timeZoneConverter,
        UserManager userManager,
        AccountLinker accountLinker,
        UserManagerWrapper userManagerWrapper,
        RequestHelper requestHelper,
        FileUploader fileUploader,
        SocketManager socketManager,
        FileDtoHelper fileDtoHelper,
        GlobalFolderHelper globalFolderHelper,
        IDistributedCache cache,
        CspSettingsHelper cspSettingsHelper,
        TenantQuotaFeatureCheckerCount<CountPaidUserFeature> countPaidUserChecker,
        StudioNotifyService studioNotifyService
        )
    {
        CommonMethods = commonMethods;
        HostedSolution = hostedSolution;
        Log = log;
        ApiSystemHelper = apiSystemHelper;
        SecurityContext = securityContext;
        Configuration = configuration;
        TenantManager = tenantManager;
        CoreSettings = coreSettings;
        ZoomMultiTenantHelper = zoomMultiTenantHelper;
        ZoomAccountHelper = zoomAccountHelper;
        TimeZonesProvider = timeZonesProvider;
        TimeZoneConverter = timeZoneConverter;
        UserManager = userManager;
        AccountLinker = accountLinker;
        UserManagerWrapper = userManagerWrapper;
        RequestHelper = requestHelper;
        FileUploader = fileUploader;
        SocketManager = socketManager;
        FileDtoHelper = fileDtoHelper;
        GlobalFolderHelper = globalFolderHelper;
        Cache = cache;
        CspSettingsHelper = cspSettingsHelper;
        CountPaidUserChecker = countPaidUserChecker;
        StudioNotifyService = studioNotifyService;
    }

    #region For TEST api

    [HttpGet("test")]
    public IActionResult Check()
    {
        return Ok(new
        {
            value = "Zoom api works"
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

        ZoomCollaborationCachedRoom collaboration = null;
        var collaborationIsActive = !string.IsNullOrWhiteSpace(model.CollaborationId) && !"none".Equals(model.CollaborationId);
        if (collaborationIsActive)
        {
            Log.LogDebug($"GetState(): Got CollaborationId, getting collaboration from cache");
            collaboration = Cache.GetCollaboration(mid);
        }

        var jwtSecret = Configuration["zoom:gate-secret"];

        string confirmLink = null;
        bool foreignTenant = false;
        int tenantId = -1;
        string tenantRegion = null;
        if (collaboration != null)
        {
            Log.LogDebug($"GetState(): Collaboration is not null, getting confirm link using tenant id {collaboration.TenantId}");
            confirmLink = await GetConfirmLinkByTenantId(collaboration.TenantId, uid);
            model.TenantId = collaboration.TenantId;
            model.TenantRegion = collaboration.TenantRegion;
            tenantId = collaboration.TenantId;
            tenantRegion = collaboration.TenantRegion;

            var ownTenant = GetTenantByAccountId(model.AccountId);
            foreignTenant = ownTenant.Id != collaboration.TenantId;
        }
        else
        {
            if (Request.Cookies.TryGetValue("ZoomLink", out var zoomLinkJwt))
            {
                try
                {
                    Log.LogDebug($"GetState(): ZoomLink Cookie is not null, getting confirm link using jwt");
                    var zoomLinkJson = JsonWebToken.Decode(zoomLinkJwt, jwtSecret);
                    var zoomLink = JsonSerializer.Deserialize<ZoomLinkCookie>(zoomLinkJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
                    confirmLink = await GetConfirmLinkByTenantId(zoomLink.TenantId, uid);
                    tenantId = zoomLink.TenantId;
                    tenantRegion = zoomLink.TenantRegion;
                }
                catch (Exception ex)
                {
                    Log.LogWarning(ex, $"GetState(): ZoomLink jwt could not be parsed");
                    Response.Cookies.Delete("ZoomLink");
                }
            }

            if (confirmLink == null)
            {
                Log.LogDebug($"GetState(): Collaboration is null, getting confirm link using account number {model.AccountId}");
                confirmLink = await GetConfirmLinkByAccountId(model.AccountId, uid);
            }
        }

        if (!forceAuth && confirmLink != null)
        {
            Log.LogDebug($"GetState(): Got request from Zoom App; Found portal and user, redirecting with auth; AccountNumber: {model.AccountId}; UserId: {uid}");

            var integrationPayload = new ZoomIntegrationPayload()
            {
                ConfirmLink = confirmLink,
                Home = Configuration["zoom:home"],
                OwnAccountId = model.AccountId,
                ForeignTenant = foreignTenant
            };

            if (collaborationIsActive)
            {
                Log.LogDebug("GetState(): Collaboration is active");

                integrationPayload.Collaboration = new ZoomCollaborationRoom()
                {
                    Status = ZoomCollaborationStatus.Pending,
                };

                if (collaboration != null)
                {
                    Log.LogDebug("GetState(): Collaboration found");
                    integrationPayload.Collaboration.FileId = collaboration.FileId;
                    integrationPayload.Collaboration.RoomId = collaboration.RoomId;
                    integrationPayload.Collaboration.Status = collaboration.Status;
                }
            }

            var link = tenantId > -1
                    ? await GetPayloadRedirectLinkByTenantId(tenantId, integrationPayload)
                    : await GetPayloadRedirectLinkByAccountId(model.AccountId, integrationPayload);

            if (noRedirect)
            {
                return Ok(link);
            }
            else
            {
                return Redirect(link);
            }
        }

        Log.LogDebug($"GetState(): ConfirmLink is null, proceeding to oauth");


        var verifier = GenerateCodeVerifier();
        var challenge = GenerateCodeChallenge(verifier);

        Cache.PutOauthVerifier(challenge, verifier);

        var payload = JsonSerializer.Serialize(new ZoomAuthPayload()
        {
            State = JsonWebToken.Encode(model, jwtSecret),
            Challenge = challenge
        });

        Log.LogDebug("GetState(): New user, returning OAuth challenge");
        if (noRedirect)
        {
            return Ok($"https{Uri.SchemeDelimiter}{Configuration["zoom:zoom-domain"]}/?payload={HttpUtility.UrlEncode(payload)}");
        }
        else
        {
            return Redirect($"https{Uri.SchemeDelimiter}{Configuration["zoom:zoom-domain"]}/?payload={HttpUtility.UrlEncode(payload)}");
        }
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
        Log.LogDebug("GetHome(): Got GET redirect from Zoom OAuth;");

        try
        {
            var jwtSecret = Configuration["zoom:gate-secret"];

            ZoomStateModel stateModel = null;
            if (state != null)
            {
                var stateJson = JsonWebToken.Decode(state, jwtSecret);
                stateModel = JsonSerializer.Deserialize<ZoomStateModel>(stateJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            }

            Log.LogDebug($"GetHome(): Got GET request from Zoom App; AccountId: {stateModel?.AccountId}; AccountNumber: {stateModel?.AccountNumber}");

            var loginProvider = ZoomAccountHelper.GetLoginProvider();
            Log.LogDebug("GetHome(): Exchanging code for AccessToken");
            var token = loginProvider.GetAccessToken(code, Configuration["zoom:zoom-redirect-uri"]);
            Log.LogDebug("GetHome(): Requesting profile info");
            var (profile, raw) = loginProvider.GetLoginProfileAndRaw(token.AccessToken);

            if (raw.AccountId.Contains('_'))
            {
                // ToDo: we might have a collision
                raw.AccountId = raw.AccountId.Replace("_", "--");
            }

            Log.LogDebug("GetHome(): Creating user and/or tenant");
            var (_, tenant) = await CreateUserAndTenant(profile, raw.AccountId, stateModel?.TenantId);

            var deeplink = CreateDeeplink(token.AccessToken);

            Log.LogDebug("GetHome(): Redirecting to ZoomClient with deeplink");
            return Redirect(deeplink);
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, $"GetHome(): Error while processing install link");
            return BadRequest();
        }
    }

    [HttpPost("link")]
    public async Task<IActionResult> PostLink(ZoomLinkPayload model)
    {
        List<Tenant> tenants;
        if (!string.IsNullOrWhiteSpace(model.Login) && !string.IsNullOrWhiteSpace(model.Password))
        {
            tenants = await ZoomMultiTenantHelper.FindTenantsAsync(model.Login, model.Password);
        }
        else
        {
            tenants = [];
        }

        var region = Configuration["zoom:aws-region"];
        var response = new ZoomLinkResponse()
        {
            Login = model.Login,
            TenantInfo = tenants.Select(t => new ZoomTenantInfo() { Id = t.Id, Name = t.Name, Domain = t.GetTenantDomain(CoreSettings), Region = region }).ToList()
        };

        var jwtSecret = Configuration["zoom:gate-secret"];

        return Ok(JsonWebToken.Encode(response, jwtSecret));
    }

    [HttpPut("link")]
    public async Task<IActionResult> PutLink(ZoomLinkPutPayload model)
    {
        var response = new ZoomIntegrationPayload()
        {
            Home = Configuration["zoom:home"]
        };

        try
        {
            var jwtSecret = Configuration["zoom:gate-secret"];

            var stateJson = JsonWebToken.Decode(model.State, jwtSecret);
            var state = JsonSerializer.Deserialize<ZoomLinkResponse>(stateJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

            var codeVerifier = Cache.GetOauthVerifier(model.Challenge);
            if (codeVerifier == null)
            {
                Log.LogDebug("PutLink(): Incorrect ouath state");
                return BadRequest("incorrect ouath state");
            }

            if (!state.TenantInfo.Any(t => t.Id == model.ChosenTenant))
            {
                return BadRequest("incorrect chosen tenant");
            }

            Log.LogDebug($"PutLink(): Got PUT request from Zoom App; TenantId: {model.ChosenTenant}; User: {state.Login}");

            var loginProvider = ZoomAccountHelper.GetLoginProvider();
            Log.LogDebug("PutLink(): Exchanging code for AccessToken");
            var token = loginProvider.GetAccessToken(model.Code, model.RedirectUri, codeVerifier);
            Log.LogDebug("PutLink(): Requesting profile info");
            var profile = loginProvider.GetLoginProfile(token);

            Log.LogDebug("PutLink(): Creating user and/or tenant");
            var tenant = await LinkUserToTenant(profile, state.Login, model.ChosenTenant);

            Log.LogDebug($"PutLink(): Setting csp settings to allow '{$"https://{tenant.Alias}.{Configuration["zoom:zoom-domain"]}"}'.");
            await AddDomainToCsp($"https://{tenant.Alias}.{Configuration["zoom:zoom-domain"]}");

            response.ConfirmLink = GetTenantRedirectUri(tenant, state.Login);
            response.Collaboration = new ZoomCollaborationRoom()
            {
                Status = ZoomCollaborationStatus.Pending,
            };

            var cookie = new ZoomLinkCookie()
            {
                TenantId = tenant.Id,
                TenantRegion = Configuration["zoom:aws-region"]
            };

            Response.Cookies.Append("ZoomLink", JsonWebToken.Encode(cookie, jwtSecret), new CookieOptions() { Domain = Configuration["zoom:zoom-domain"], Expires = DateTimeOffset.Now.AddDays(30) });

            Log.LogDebug("PutLink(): Returning user with confirmLink");
            return Ok(GetPayloadRedirectLink(tenant, response));
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, $"PutLink(): Error while linking");
            response.Error = ex.Message;
            return BadRequest(response);
        }
    }

    [HttpPost("home")]
    [AllowCrossSiteJson]
    public async Task<IActionResult> PostHome(ZoomHomeModel model)
    {
        var response = new ZoomIntegrationPayload()
        {
            Home = Configuration["zoom:home"]
        };

        try
        {
            var jwtSecret = Configuration["zoom:gate-secret"];

            var stateJson = JsonWebToken.Decode(model.State, jwtSecret);
            var state = JsonSerializer.Deserialize<ZoomStateModel>(stateJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });

            var codeVerifier = Cache.GetOauthVerifier(model.Challenge);
            if (codeVerifier == null)
            {
                Log.LogDebug("PostHome(): Incorrect ouath state");
                return BadRequest("incorrect ouath state");
            }

            Log.LogDebug($"PostHome(): Got POST request from Zoom App; AccountId: {state.AccountId}; AccountNumber: {state.AccountNumber}");

            var loginProvider = ZoomAccountHelper.GetLoginProvider();
            Log.LogDebug("PostHome(): Exchanging code for AccessToken");
            var token = loginProvider.GetAccessToken(model.Code, model.RedirectUri, codeVerifier);
            Log.LogDebug("PostHome(): Requesting profile info");
            var (profile, raw) = loginProvider.GetLoginProfileAndRaw(token.AccessToken);

            if (raw.AccountId.Contains('_'))
            {
                // ToDo: we might have a collision
                raw.AccountId = raw.AccountId.Replace("_", "--");
            }

            Log.LogDebug("PostHome(): Creating user and/or tenant");
            var (_, tenant) = await CreateUserAndTenant(profile, raw.AccountId, state.TenantId);

            response.ConfirmLink = GetTenantRedirectUri(tenant, profile.EMail);
            if (!string.IsNullOrWhiteSpace(state.CollaborationId) && !"none".Equals(state.CollaborationId))
            {
                Log.LogDebug($"PostHome(): Got collaboration ID {state.CollaborationId}, setting status to pending");
                response.Collaboration = new ZoomCollaborationRoom()
                {
                    Status = ZoomCollaborationStatus.Pending,
                };
            }

            Log.LogDebug("PostHome(): Returning user with confirmLink");
            Response.Cookies.Delete("ZoomLink", new CookieOptions() { Domain = Configuration["zoom:zoom-domain"], Expires = DateTimeOffset.MinValue });
            return Ok(GetPayloadRedirectLink(tenant, response));
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, $"PostHome(): Error while creating");
            response.Error = ex.Message;
            return BadRequest(response);
        }
    }

    [HttpPost("upload")]
    [Authorize(AuthenticationSchemes = ZoomAuthHandler.ZOOM_AUTH_SCHEME_HEADER)]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        var uid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_UID)?.Value;
        var mid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_MID)?.Value;

        var userId = await ZoomAccountHelper.GetUserIdFromZoomUid(uid);
        if (userId == null)
        {
            return Unauthorized();
        }

        try
        {
            await SecurityContext.AuthenticateMeWithoutCookieAsync(userId.Value);

            if (file.Length > GetUploadLimit())
            {
                return BadRequest();
            }

            try
            {
                var resultFile = await FileUploader.ExecAsync(await GlobalFolderHelper.FolderMyAsync, file.FileName, file.Length, file.OpenReadStream(), true, true);

                await SocketManager.CreateFileAsync(resultFile);

                return Ok(await FileDtoHelper.GetAsync(resultFile));
            }
            catch (FileNotFoundException e)
            {
                Log.LogWarning(e, "Uploading file failed");
                return BadRequest();
            }
        }
        catch (TenantQuotaException)
        {
            return Ok(new { error = "quota" });
        }
        finally
        {
            SecurityContext.Logout();
        }
    }

    [HttpPost("deauth")]
    [Authorize(AuthenticationSchemes = ZoomHookAuthHandler.ZOOM_HOOK_AUTH_SCHEME)]
    public async Task<IActionResult> DeauthorizationHook(ZoomEventModel<ZoomDeauthorizationModel> zoomEvent)
    {
        try
        {
            await SecurityContext.AuthenticateMeWithoutCookieAsync(Core.Configuration.Constants.CoreSystem);
            Log.LogInformation($"DeauthorizationHook(): Got deauth request with zoom user id {zoomEvent.Payload.UserId}");
            // getting all linked accounts on all tenants
            var userIds = await AccountLinker.GetLinkedProfilesAsync(zoomEvent.Payload.UserId, ProviderConstants.Zoom);

            foreach (var userId in userIds)
            {
                try
                {
                    Log.LogInformation($"DeauthorizationHook(): Unlinking user with zoom id {zoomEvent.Payload.UserId}, user id {userId}");
                    await AccountLinker.RemoveProviderAsync(userId.ToString(), ProviderConstants.Zoom);
                }
                catch (Exception ex)
                {
                    Log.LogError(ex, $"DeauthorizationHook(): Coulnd't unlink user with zoom id {zoomEvent.Payload.UserId}, user id {userId}");
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            Log.LogError(ex, $"DeauthorizationHook(): Error");
        }
        finally
        {
            SecurityContext.Logout();
        }
        return BadRequest();
    }

    #endregion

    #region private methods

    private long GetUploadLimit()
    {
        var limitString = Configuration["zoom:zoom-upload-limit"];
        if (!string.IsNullOrEmpty(limitString))
        {
            if (long.TryParse(limitString, out var limit))
            {
                return limit;
            }
        }
        return 10 * 1024 * 1024;
    }

    private async Task<string> GetPayloadRedirectLinkByTenantId(int tenantId, ZoomIntegrationPayload payload)
    {
        var tenant = await HostedSolution.GetTenantAsync(tenantId);
        return GetPayloadRedirectLink(tenant, payload);
    }

    private async Task<string> GetPayloadRedirectLinkByAccountId(string accountId, ZoomIntegrationPayload payload)
    {
        var portalName = GenerateAlias(accountId);
        var tenant = await HostedSolution.GetTenantAsync(portalName);
        return GetPayloadRedirectLink(tenant, payload);
    }

    private string GetPayloadRedirectLink(Tenant tenant, ZoomIntegrationPayload payload)
    {
        payload.DocSpaceUrl = $"https{Uri.SchemeDelimiter}{tenant.GetTenantDomain(CoreSettings)}";
        var serialized = JsonSerializer.Serialize(payload);

        var link = $"https{Uri.SchemeDelimiter}{tenant.Alias}.{Configuration["zoom:zoom-domain"]}/?payload={HttpUtility.UrlEncode(serialized)}";
        Log.LogDebug($"GetPayloadRedirectLink(): Generated confirmLink ({link})");
        return link;
    }

    private string CreateDeeplink(string accessToken)
    {
        var json = RequestHelper.PerformRequest($"{ZoomLoginProvider.ApiUrl}/zoomapp/deeplink/",
            "application/json",
            "POST",
            JsonSerializer.Serialize(new { action = "open" }),
            new Dictionary<string, string>()
            {
                { "Authorization", $"Bearer {accessToken}" }
            });
        return JsonNode.Parse(json)["deeplink"].GetValue<string>();
    }

    private async Task<(UserInfo, Tenant)> CreateUserAndTenant(LoginProfile profile, string accountId, int? tenantId = null)
    {
        var portalName = GenerateAlias(accountId);
        var tenant = await HostedSolution.GetTenantAsync(portalName);
        bool guest = false;
        if (tenantId.HasValue)
        {
            if (tenant != null && tenant.Id == tenantId.Value)
            {
                Log.LogDebug($"CreateUserAndTenant(): TenantId equals accountNumber, no additional actions needed");
            }
            else
            {
                Log.LogDebug($"CreateUserAndTenant(): TenantId not equals accountNumber, adding user as a guest");
                guest = true;
                tenant = await HostedSolution.GetTenantAsync(tenantId.Value);
            }
        }

        var newTenant = false;
        if (tenant == null)
        {
            Log.LogDebug($"CreateUserAndTenant(): Portal '{portalName}' wasn't found, creating;");
            tenant = await CreateTenant(portalName, profile);
            Log.LogInformation($"CreateUserAndTenant(): Portal '{portalName}' created.");
            newTenant = true;
        }

        TenantManager.SetCurrentTenant(tenant);
        try
        {
            await SecurityContext.AuthenticateMeWithoutCookieAsync(Core.Configuration.Constants.CoreSystem);

            var shouldLink = false || newTenant;
            var userInfo = await UserManager.GetUserByEmailAsync(profile.EMail);
            if (!UserManager.UserExists(userInfo.Id))
            {
                Log.LogDebug($"CreateUserAndTenant(): Creating new user for portal '{portalName}'; UserId: {profile.HashId}");
                userInfo = await CreateUser(profile, guest);
                Log.LogInformation($"CreateUserAndTenant(): Created new user for '{portalName}'; UserId: {profile.HashId}");
                shouldLink = true;
            }
            else
            {
                var linkedUserId = await ZoomAccountHelper.GetUserIdFromZoomUid(profile.Id);
                shouldLink = linkedUserId == null;
            }

            if (shouldLink)
            {
                Log.LogDebug($"CreateUserAndTenant(): Linking portal user '{userInfo.Id}' to zoom user '{profile.Id}'.");

                var links = await AccountLinker.GetLinkedProfilesAsync(userInfo.Id.ToString(), ProviderConstants.Zoom);
                if (links.Any())
                {
                    Log.LogInformation($"CreateUserAndTenant(): Portal user '{userInfo.Id}' already has zoom link.");
                    throw new Exception("User already linked");
                }

                await AccountLinker.AddLinkAsync(userInfo.Id, profile);
                Log.LogInformation($"CreateUserAndTenant(): Linked portal user '{userInfo.Id}' to zoom user '{profile.Id}'.");
            }

            return (userInfo, tenant);
        }
        catch (Exception ex)
        {
            Log.LogDebug(ex, $"CreateUserAndTenant(): Error");
            throw;
        }
        finally
        {
            SecurityContext.Logout();
        }
    }

    private async Task<Tenant> LinkUserToTenant(LoginProfile profile, string email, int tenantId)
    {
        var tenant = await HostedSolution.GetTenantAsync(tenantId);
        TenantManager.SetCurrentTenant(tenant);
        try
        {
            await SecurityContext.AuthenticateMeWithoutCookieAsync(Core.Configuration.Constants.CoreSystem);

            var linkedUserId = await ZoomAccountHelper.GetUserIdFromZoomUid(profile.Id);
            if (linkedUserId != null)
            {
                Log.LogInformation($"LinkUserToTenant(): User already linked.");
                return tenant;
            }

            var userInfo = await UserManager.GetUserByEmailAsync(email);
            if (!UserManager.UserExists(userInfo.Id))
            {
                Log.LogInformation($"LinkUserToTenant(): User not found with email '{email}', tenant '{tenantId}'");
                throw new Exception("User not found");
            }

            Log.LogDebug($"LinkUserToTenant(): Linking portal user '{userInfo.Id}' to zoom user '{profile.Id}'.");
            await AccountLinker.AddLinkAsync(userInfo.Id, profile);
            Log.LogInformation($"LinkUserToTenant(): Linked portal user '{userInfo.Id}' to zoom user '{profile.Id}'.");

            return tenant;
        }
        finally
        {
            SecurityContext.Logout();
        }
    }

    private string SanitizeName(string name, string defaultValue)
    {
        var regex = new Regex(Configuration["core:username:regex"] ?? "");
        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultValue;
        }
        else
        {
            return new string(name.Select(s => regex.Match(s.ToString()).Success ? s : '-').ToArray());
        }
    }

    private CultureInfo GetCultureFromLocale(string locale)
    {
        var culture = TimeZonesProvider.GetCurrentCulture(null);
        if (!string.IsNullOrWhiteSpace(locale))
        {
            culture = TimeZonesProvider.GetCurrentCulture(locale.Split('-').First());
        }
        return culture;
    }

    private async Task<UserInfo> CreateUser(LoginProfile profile, bool guest)
    {
        Log.LogDebug($"CreateTenant(): Creating user for zoom user '{profile.Id}'.");
        var userInfo = new UserInfo
        {
            FirstName = SanitizeName(profile.FirstName, UserControlsCommonResource.UnknownFirstName),
            LastName = SanitizeName(profile.LastName, UserControlsCommonResource.UnknownLastName),
            Email = profile.EMail,
            Title = string.Empty,
            Location = string.Empty,
            CultureName = GetCultureFromLocale(profile.Locale).Name,
            ActivationStatus = EmployeeActivationStatus.Activated,
            Status = EmployeeStatus.Active,
        };

        EmployeeType employeeType = EmployeeType.RoomAdmin;
        if (guest)
        {
            employeeType = EmployeeType.User;
        }
        else
        {
            try
            {
                await CountPaidUserChecker.CheckAppend();
            }
            catch (TenantQuotaException)
            {
                Log.LogDebug($"CreateTenant(): Quota exceeded adding as simple user.");
                employeeType = EmployeeType.User;
            }
        }

        HttpContext.Request.Scheme = "https";
        HttpContext.Request.Host = new HostString(TenantManager.GetCurrentTenant().GetTenantDomain(CoreSettings));
        return await UserManagerWrapper.AddUserAsync(userInfo, UserManagerWrapper.GeneratePassword(), type: employeeType, afterInvite: true, notify: true);
    }

    private async Task<Tenant> CreateTenant(string portalName, LoginProfile profile)
    {
        Log.LogDebug($"CreateTenant(): Creating tenant with name '{portalName}' for zoom user '{profile.Id}'.");
        var info = new TenantRegistrationInfo
        {
            Name = "Zoom",
            Address = portalName,
            Culture = GetCultureFromLocale(profile.Locale),
            FirstName = SanitizeName(profile.FirstName, UserControlsCommonResource.UnknownFirstName),
            LastName = SanitizeName(profile.LastName, UserControlsCommonResource.UnknownLastName),
            PasswordHash = null,
            Email = profile.EMail,
            TimeZoneInfo = TimeZoneConverter.GetTimeZone(profile.TimeZone) ?? TimeZoneInfo.Local,
            MobilePhone = null,
            Industry = TenantIndustry.Other,
            Spam = false,
            Calls = false,
            ActivationStatus = EmployeeActivationStatus.Activated,
        };

        Log.LogDebug($"CreateTenant(): Registering tenant {portalName}.");
        var tenant = await HostedSolution.RegisterTenantAsync(info);

        TenantManager.SetCurrentTenant(tenant);

        var domain = tenant.GetTenantDomain(CoreSettings);
        if (ApiSystemHelper.ApiCacheEnable)
        {
            var region = Configuration["zoom:aws-region"];
            Log.LogDebug($"CreateTenant(): Adding tenant to cache {domain} {region}.");
            await ApiSystemHelper.AddTenantToCacheAsync(domain, region);
        }

        HttpContext.Request.Scheme = "https";
        HttpContext.Request.Host = new HostString(domain);
        await SendCongratulations(tenant, $"https://{domain}");

        try
        {
            Log.LogDebug($"CreateTenant(): Setting tariff for tenant {tenant.Id}.");

            if (TryGetQuotaId(out var trialQuotaId))
            {
                var dueDate = DateTime.MaxValue;
                if (TryGetQuotaDue(out var dueTrial))
                {
                    dueDate = DateTime.UtcNow.AddDays(dueTrial);
                }

                var tariff = new Tariff
                {
                    Quotas = new List<Quota> { new Quota(trialQuotaId, 1) },
                    DueDate = dueDate
                };
                await HostedSolution.SetTariffAsync(tenant.Id, tariff);
            }

            Log.LogDebug($"CreateTenant(): Setting csp settings to allow '{$"https://{portalName}.{Configuration["zoom:zoom-domain"]}"}'.");
            await AddDomainToCsp($"https://{portalName}.{Configuration["zoom:zoom-domain"]}");
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "CreateTenant(): Exception while creating tenant.");
        }

        Log.LogInformation($"CreateTenant(): Created tenant {portalName} with id {tenant.Id}.");
        return tenant;
    }

    private async Task SendCongratulations(Tenant tenant, string domain)
    {
        try
        {
            Log.LogInformation("Sending welcome email");
            var user = await UserManager.GetUserAsync(tenant.OwnerId, null);

            await StudioNotifyService.SendZoomWelcomeAsync(user, domain);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "Error while sending welcome email");
        }
    }

    private async Task AddDomainToCsp(string domain)
    {
        var domains = await CspSettingsHelper.LoadAsync();

        if (domains == null || domains.Domains == null || !domains.Domains.Any())
        {
            await CspSettingsHelper.SaveAsync(new List<string>() { domain });
        }
        else
        {
            await CspSettingsHelper.SaveAsync(new List<string>(domains.Domains) { domain });
        }
    }

    private bool TryGetQuotaId(out int quotaId)
    {
        if (!int.TryParse(Configuration["zoom:quota:id"], out int parsedId))
        {
            if (!int.TryParse(Configuration["quota:id"], out parsedId))
            {
                quotaId = 0;
                return false;
            }
        }
        quotaId = parsedId;
        return true;
    }

    private bool TryGetQuotaDue(out int quotaDue)
    {
        if (!int.TryParse(Configuration["zoom:quota:due"], out int parsedDue))
        {
            if (!int.TryParse(Configuration["quota:due"], out parsedDue))
            {
                quotaDue = 0;
                return false;
            }
        }
        quotaDue = parsedDue;
        return true;
    }

    private string GenerateAlias(string accountId)
    {
        return $"zoom-{accountId}";
    }

    private async Task<string> GetConfirmLinkByTenantId(int tenantId, string uid)
    {
        ArgumentException.ThrowIfNullOrEmpty(uid, nameof(uid));

        var tenant = await HostedSolution.GetTenantAsync(tenantId);

        Log.LogDebug($"GetConfirmLinkByTenantId(): Getting confirm link with tenant {tenant?.Id}, user {uid}.");
        return await GetConfirmLink(tenant, uid);
    }

    private async Task<Tenant> GetTenantByAccountId(string accountId)
    {
        var portalName = GenerateAlias(accountId);
        var tenant = await HostedSolution.GetTenantAsync(portalName);

        return tenant;
    }

    private async Task<string> GetConfirmLinkByAccountId(string accountId, string uid)
    {
        ArgumentException.ThrowIfNullOrEmpty(uid, nameof(uid));

        var tenant = await GetTenantByAccountId(accountId);

        Log.LogDebug($"GetConfirmLinkByAccountNumber(): Getting confirm link with tenant {tenant?.Id}, user {uid}.");
        return await GetConfirmLink(tenant, uid);
    }

    private async Task<string> GetConfirmLink(Tenant tenant, string uid)
    {
        if (tenant == null)
        {
            Log.LogDebug("GetConfirmLink(): Tenant is null.");
            return null;
        }

        TenantManager.SetCurrentTenant(tenant);

        Log.LogDebug($"GetConfirmLink(): Getting userId from by zoom uid {uid}.");
        var userId = await ZoomAccountHelper.GetUserIdFromZoomUid(uid);
        if (userId == null)
        {
            Log.LogDebug("GetConfirmLink(): User is null.");
            return null;
        }

        Log.LogDebug("GetConfirmLink(): Found user.");
        var user = await UserManager.GetUserAsync(userId.Value, null);

        return GetTenantRedirectUri(tenant, user.Email);
    }

    private string GetTenantRedirectUri(Tenant tenant, string email)
    {
        // ToDo: change https to Request.Scheme
        return CommonMethods.CreateReference(tenant.Id, "https", tenant.GetTenantDomain(CoreSettings), email, false);
    }

    private string GenerateCodeVerifier()
    {
        var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Base64UrlEncoder.Encode(bytes);
    }

    private string GenerateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier));
        return Base64UrlEncoder.Encode(bytes);
    }

    private class ZoomAuthPayload
    {
        public string State { get; set; }
        public string Challenge { get; set; }
    }

    #endregion


    // ToDo: remove; hack to register services
    [Scope]
    [ApiController]
    [Route("[controller")]
    public class ZoomHackController : ControllerBase
    {
        public ZoomHackController(FileStorageService fileStorageService, CustomTagsService tagsService, GlobalFolderHelper globalFolderHelper, ZoomBackupHelper zoomBackupHelper)
        {
        }
    }
}
