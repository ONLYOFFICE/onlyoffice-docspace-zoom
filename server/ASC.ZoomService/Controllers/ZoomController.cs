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
using ASC.ZoomService.Extensions;
using ASC.ZoomService.Helpers;
using ASC.ZoomService.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

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
    private TimeZonesProvider TimeZonesProvider { get; }
    private TimeZoneConverter TimeZoneConverter { get; }
    private UserManager UserManager { get; }
    private AccountLinker AccountLinker { get; }
    private UserManagerWrapper UserManagerWrapper { get; }
    private RequestHelper RequestHelper { get; }
    private FileStorageService FileStorageService { get; }
    private SettingsManager SettingsManager { get; }
    private FileUploader FileUploader { get; }
    private SocketManager SocketManager { get; }
    private FileDtoHelper FileDtoHelper { get; }
    private GlobalFolderHelper GlobalFolderHelper { get; }
    private IDistributedCache Cache { get; }
    private CspSettingsHelper CspSettingsHelper { get; }
    private TenantQuotaFeatureCheckerCount<CountPaidUserFeature> CountPaidUserChecker { get; }

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
        TimeZonesProvider timeZonesProvider,
        TimeZoneConverter timeZoneConverter,
        UserManager userManager,
        AccountLinker accountLinker,
        UserManagerWrapper userManagerWrapper,
        RequestHelper requestHelper,
        FileStorageService fileStorageService,
        SettingsManager settingsManager,
        FileUploader fileUploader,
        SocketManager socketManager,
        FileDtoHelper fileDtoHelper,
        GlobalFolderHelper globalFolderHelper,
        IDistributedCache cache,
        CspSettingsHelper cspSettingsHelper,
        TenantQuotaFeatureCheckerCount<CountPaidUserFeature> countPaidUserChecker
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
        ZoomAccountHelper = zoomAccountHelper;
        TimeZonesProvider = timeZonesProvider;
        TimeZoneConverter = timeZoneConverter;
        UserManager = userManager;
        AccountLinker = accountLinker;
        UserManagerWrapper = userManagerWrapper;
        RequestHelper = requestHelper;
        FileStorageService = fileStorageService;
        SettingsManager = settingsManager;
        FileUploader = fileUploader;
        SocketManager = socketManager;
        FileDtoHelper = fileDtoHelper;
        GlobalFolderHelper = globalFolderHelper;
        Cache = cache;
        CspSettingsHelper = cspSettingsHelper;
        CountPaidUserChecker = countPaidUserChecker;
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
    public async Task<IActionResult> GetState([FromQuery] ZoomStateModel model, [FromQuery] bool noRedirect = false)
    {
        var uid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_UID)?.Value;
        var mid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_MID)?.Value;
        model.TenantId = null;

        ZoomCollaborationCachedRoom collaboration = null;
        var collaborationIsActive = !string.IsNullOrWhiteSpace(model.CollaborationId) && !"none".Equals(model.CollaborationId);
        if (collaborationIsActive)
        {
            Log.LogDebug($"GetState(): Got CollaborationId, getting collaboration from cache");
            collaboration = Cache.GetCollaboration(mid);
        }

        string confirmLink;
        bool foreignTenant = false;
        if (collaboration != null)
        {
            Log.LogDebug($"GetState(): Collaboration is not null, getting confirm link using tenant id {collaboration.TenantId}");
            confirmLink = await GetConfirmLinkByTenantId(collaboration.TenantId, uid);
            model.TenantId = collaboration.TenantId;

            var ownTenant = GetTenantByAccountId(model.AccountId);
            foreignTenant = ownTenant.Id != collaboration.TenantId;
        }
        else
        {
            Log.LogDebug($"GetState(): Collaboration is null, getting confirm link using account number {model.AccountId}");
            confirmLink = await GetConfirmLinkByAccountId(model.AccountId, uid);
        }

        if (confirmLink != null)
        {
            Log.LogDebug($"GetState(): Got request from Zoom App; Found portal and user, redirecting with auth; AccountNumber: {model.AccountId}; UserId: {uid}");

            var integrationPayload = new ZoomIntegrationPayload()
            {
                ConfirmLink = confirmLink,
                Home = Configuration["zoom:home"],
                OwnAccountId = foreignTenant ? model.AccountId : null
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

            var link = collaboration != null
                    ? GetPayloadRedirectLinkByTenantId(collaboration.TenantId, integrationPayload)
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

        var jwtSecret = Configuration["zoom:gate-secret"];

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
    public IActionResult GetInstall()
    {
        return Redirect($"https://zoom.us/oauth/authorize?response_type=code&client_id={ZoomAccountHelper.GetLoginProvider().ClientID}&redirect_uri={Configuration["zoom:zoom-redirect-uri"]}");
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome([FromQuery] string code)
    {
        Log.LogDebug("GetHome(): Got GET redirect from Zoom OAuth;");

        try
        {
            var loginProvider = ZoomAccountHelper.GetLoginProvider();
            Log.LogDebug("GetHome(): Exchanging code for AccessToken");
            var token = loginProvider.GetAccessToken(code, Configuration["zoom:zoom-redirect-uri"]);
            Log.LogDebug("GetHome(): Requesting profile info");
            var (profile, raw) = loginProvider.GetLoginProfileAndRaw(token.AccessToken);
            Log.LogDebug("GetHome(): Creating user and/or tenant");
            var (_, tenant) = await CreateUserAndTenant(profile, raw.AccountId);

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
            var profile = loginProvider.GetLoginProfile(token);

            Log.LogDebug("PostHome(): Creating user and/or tenant");
            var (_, tenant) = await CreateUserAndTenant(profile, state.AccountId, state.TenantId);

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
            return Ok(GetPayloadRedirectLink(tenant, response));
        }
        catch (Exception ex)
        {
            Log.LogDebug($"PostHome(): Error: {ex.Message}");
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
            var userIds = await AccountLinker.GetLinkedObjectsAsync(zoomEvent.Payload.UserId, ProviderConstants.Zoom);

            foreach (var userId in userIds)
            {
                try
                {
                    Log.LogInformation($"DeauthorizationHook(): Unlinking user with zoom id {zoomEvent.Payload.UserId}, user id {userId}");
                    await AccountLinker.RemoveLinkAsync(userId.ToString(), zoomEvent.Payload.UserId, ProviderConstants.Zoom);
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

    private string GetPayloadRedirectLinkByTenantId(int tenantId, ZoomIntegrationPayload payload)
    {
        var tenant = HostedSolution.GetTenant(tenantId);
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
            if (tenant.Id == tenantId.Value)
            {
                Log.LogDebug($"CreateUserAndTenant(): TenantId equals accountNumber, no additional actions needed");
            }
            else
            {
                Log.LogDebug($"CreateUserAndTenant(): TenantId not equals accountNumber, adding user as a guest");
                guest = true;
                tenant = HostedSolution.GetTenant(tenantId.Value);
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
                Log.LogDebug($"CreateUserAndTenant(): Creating new user for portal '{portalName}'; UserId: {profile.UniqueId}");
                userInfo = await CreateUser(profile, guest);
                Log.LogInformation($"CreateUserAndTenant(): Created new user for '{portalName}'; UserId: {profile.UniqueId}");
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
                await AccountLinker.AddLinkAsync(userInfo.Id.ToString(), profile);
                Log.LogInformation($"CreateUserAndTenant(): Linked portal user '{userInfo.Id}' to zoom user '{profile.Id}'.");
            }

            return (userInfo, tenant);
        }
        finally
        {
            SecurityContext.Logout();
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
            FirstName = string.IsNullOrEmpty(profile.FirstName) ? UserControlsCommonResource.UnknownFirstName : profile.FirstName,
            LastName = string.IsNullOrEmpty(profile.LastName) ? UserControlsCommonResource.UnknownLastName : profile.LastName,
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
        return await UserManagerWrapper.AddUserAsync(userInfo, UserManagerWrapper.GeneratePassword(), type: employeeType, afterInvite: true, notify: false);
    }

    private async Task<Tenant> CreateTenant(string portalName, LoginProfile profile)
    {
        Log.LogDebug($"CreateTenant(): Creating tenant with name '{portalName}' for zoom user '{profile.Id}'.");
        var info = new TenantRegistrationInfo
        {
            Name = "Zoom",
            Address = portalName,
            Culture = GetCultureFromLocale(profile.Locale),
            FirstName = string.IsNullOrEmpty(profile.FirstName) ? UserControlsCommonResource.UnknownFirstName : profile.FirstName,
            LastName = string.IsNullOrEmpty(profile.LastName) ? UserControlsCommonResource.UnknownLastName : profile.LastName,
            PasswordHash = null,
            Email = profile.EMail,
            TimeZoneInfo = TimeZoneConverter.GetTimeZone(profile.TimeZone) ?? TimeZoneInfo.Local,
            MobilePhone = null,
            Industry = TenantIndustry.Other,
            Spam = false,
            Calls = false,
        };

        Log.LogDebug($"CreateTenant(): Registering tenant {portalName}.");
        var tenant = await HostedSolution.RegisterTenantAsync(info);

        TenantManager.SetCurrentTenant(tenant);

        if (ApiSystemHelper.ApiCacheEnable)
        {
            var region = Configuration["zoom:aws-region"];
            var domain = tenant.GetTenantDomain(CoreSettings);
            Log.LogDebug($"CreateTenant(): Adding tenant to cache {domain} {region}.");
            await ApiSystemHelper.AddTenantToCacheAsync(domain, region);
        }

        try
        {
            Log.LogDebug($"CreateTenant(): Setting tariff for tenant {tenant.Id}.");
            var trialQuota = Configuration["quota:id"];
            if (!string.IsNullOrEmpty(trialQuota))
            {
                if (int.TryParse(trialQuota, out var trialQuotaId))
                {
                    var dueDate = DateTime.MaxValue;
                    if (int.TryParse(Configuration["quota:due"], out var dueTrial))
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
            }

            Log.LogDebug($"CreateTenant(): Setting csp settings to allow '{$"https://{portalName}.{Configuration["zoom:zoom-domain"]}"}'.");

            await CspSettingsHelper.SaveAsync(new List<string>() { $"https://{portalName}.{Configuration["zoom:zoom-domain"]}" }, false);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "CreateTenant(): Exception while creating tenant.");
        }

        Log.LogInformation($"CreateTenant(): Created tenant {portalName} with id {tenant.Id}.");
        return tenant;
    }

    private string GenerateAlias(string accountId)
    {
        return $"zoom-{accountId}";
    }

    private async Task<string> GetConfirmLinkByTenantId(int tenantId, string uid)
    {
        ArgumentException.ThrowIfNullOrEmpty(uid, nameof(uid));

        var tenant = HostedSolution.GetTenant(tenantId);

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
