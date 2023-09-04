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
using ASC.Core.Common.Security;
using ASC.FederatedLogin;
using ASC.FederatedLogin.Helpers;
using ASC.FederatedLogin.LoginProviders;
using ASC.FederatedLogin.Profile;
using ASC.Files.Core.ApiModels.ResponseDto;
using ASC.Files.Core.VirtualRooms;
using ASC.Web.Core.Files;
using ASC.Web.Files.Classes;
using ASC.Web.Files.Services.WCFService;
using ASC.Web.Files.Utils;
using ASC.ZoomService.Extensions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using static System.Net.WebRequestMethods;

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
    private FileStorageService<int> FileStorageService { get; }
    private SettingsManager SettingsManager { get; }
    private FileUploader FileUploader { get; }
    private SocketManager SocketManager { get; }
    private FileDtoHelper FileDtoHelper { get; }
    private GlobalFolderHelper GlobalFolderHelper { get; }
    private IDistributedCache Cache { get; }

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
        FileStorageService<int> fileStorageService,
        SettingsManager settingsManager,
        FileUploader fileUploader,
        SocketManager socketManager,
        FileDtoHelper fileDtoHelper,
        GlobalFolderHelper globalFolderHelper,
        IDistributedCache cache
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

    #endregion

    #region API methods

    [HttpGet("state")]
    [AllowCrossSiteJson]
    [Authorize(AuthenticationSchemes = ZoomAuthHandler.ZOOM_AUTH_SCHEME_HEADER)]
    public async Task<IActionResult> GetState([FromQuery] ZoomStateModel model)
    {
        var uid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_UID)?.Value;
        var mid = User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_MID)?.Value;
        var confirmLink = GetConfirmLink(model.AccountNumber, uid);
        if (confirmLink != null)
        {
            Log.LogDebug($"GetState(): Got request from Zoom App; Found portal and user, redirecting with auth; AccountNumber: {model.AccountNumber}; UserId: {uid}");

            var integrationPayload = new ZoomIntegrationPayload()
            {
                ConfirmLink = confirmLink,
                Home = Configuration["zoom:home"]
            };

            if (!string.IsNullOrWhiteSpace(model.CollaborationId) && !"none".Equals(model.CollaborationId))
            {
                Log.LogDebug("GetState(): CollaborationId is not null, getting collaboration from cache");
                ZoomCollaborationCachedRoom collaboration = null;

                collaboration = Cache.GetCollaboration(mid);

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

                if (integrationPayload.Collaboration.RoomId != null)
                {
                    try
                    {
                        Log.LogDebug("GetState(): Collaboration RoomId is not null, adding user to room");
                        SecurityContext.AuthenticateMeWithoutCookie(Core.Configuration.Constants.CoreSystem);
                        var access = collaboration.CollaborationType switch
                        {
                            ZoomCollaborationType.Edit => Files.Core.Security.FileShare.Collaborator,
                            _ => Files.Core.Security.FileShare.Read,
                        };
                        await FileStorageService.SetAceObjectAsync(new AceCollection<int>()
                        {
                            Message = string.Empty,
                            Files = Array.Empty<int>(),
                            Folders = new[] { int.Parse(collaboration.RoomId) },
                            Aces = new List<AceWrapper>
                            {
                                new()
                                {
                                    Id = ZoomAccountHelper.GetUserIdFromZoomUid(uid).Value,
                                    Access = access,
                                }
                            }
                        }, false);
                    }
                    finally
                    {
                        SecurityContext.Logout();
                    }
                }
            }

            return Redirect(GetPayloadRedirectLink(model.AccountNumber, integrationPayload));
        }

        // proceed to oauth

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
        return Redirect($"/zoom-ds/?payload={HttpUtility.UrlEncode(payload)}");
    }

    [HttpGet("home")]
    public async Task<IActionResult> GetHome([FromQuery] string code)
    {
        Log.LogDebug("GetHome(): Got GET redirect from Zoom OAuth;");

        try
        {
            var loginProvider = ZoomAccountHelper.GetLoginProvider();
            Log.LogDebug("GetHome(): Exchanging code for AccessToken");
            var token = loginProvider.GetAccessToken(code, loginProvider.ApiRedirectUri);
            Log.LogDebug("GetHome(): Requesting profile info");
            var (profile, raw) = loginProvider.GetLoginProfileAndRaw(token.AccessToken);
            Log.LogDebug("GetHome(): Creating user and/or tenant");
            var (_, tenant) = await CreateUserAndTenant(profile, raw.AccountNumber);

            var deeplink = CreateDeeplink(token.AccessToken);

            Log.LogDebug("GetHome(): Redirecting to ZoomClient with deeplink");
            return Redirect(deeplink);
        }
        catch (Exception ex)
        {
            Log.LogDebug($"GetHome(): Error: {ex.Message}");
            return BadRequest(ex.Message);
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
            var (_, tenant) = await CreateUserAndTenant(profile, state.AccountNumber);

            response.ConfirmLink = GetTenantRedirectUri(tenant, profile.EMail);
            response.Collaboration = new ZoomCollaborationRoom()
            {
                Status = ZoomCollaborationStatus.Pending,
            };

            Log.LogDebug("PostHome(): Returning user with confirmLink");
            return Ok(GetPayloadRedirectLink(state.AccountNumber, response));
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

        var userId = ZoomAccountHelper.GetUserIdFromZoomUid(uid);
        if (userId == null)
        {
            return Unauthorized();
        }


        try
        {
            SecurityContext.AuthenticateMeWithoutCookie(userId.Value);

            // add limit
            if (file.Length > long.MaxValue)
            {
                return BadRequest();
            }

            try
            {
                var resultFile = await FileUploader.ExecAsync(GlobalFolderHelper.FolderMy, file.FileName, file.Length, file.OpenReadStream(), true, true);

                await SocketManager.CreateFileAsync(resultFile);

                return Ok(await FileDtoHelper.GetAsync(resultFile));
            }
            catch (FileNotFoundException e)
            {
                return BadRequest();
            }

        }
        finally
        {
            SecurityContext.Logout();
        }
    }

    #endregion

    #region private methods

    private string GetPayloadRedirectLink(long accountNumber, ZoomIntegrationPayload payload)
    {
        var portalName = GenerateAlias(accountNumber);
        var tenant = HostedSolution.GetTenant(portalName);
        payload.DocSpaceUrl = $"https{Uri.SchemeDelimiter}{tenant.GetTenantDomain(CoreSettings)}";
        var serialized = JsonSerializer.Serialize(payload);

        var link = $"https{Uri.SchemeDelimiter}{Configuration["zoom:zoom-domain"]}/?payload={HttpUtility.UrlEncode(serialized)}";
        Log.LogDebug($"PostHome(): Generated confirmLink ({link})");
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

    private async Task<(UserInfo, Tenant)> CreateUserAndTenant(LoginProfile profile, long accountNumber)
    {
        var portalName = GenerateAlias(accountNumber);
        var tenant = HostedSolution.GetTenant(portalName);

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
            SecurityContext.AuthenticateMeWithoutCookie(Core.Configuration.Constants.CoreSystem);

            var shouldLink = false || newTenant;
            var userInfo = UserManager.GetUserByEmail(profile.EMail);
            if (!UserManager.UserExists(userInfo.Id))
            {
                Log.LogDebug($"CreateUserAndTenant(): Creating new user for portal '{portalName}'; UserId: {profile.UniqueId}");
                userInfo = await CreateUser(profile);
                Log.LogInformation($"CreateUserAndTenant(): Created new user for '{portalName}'; UserId: {profile.UniqueId}");
                shouldLink = true;
            }

            if (shouldLink)
            {
                Log.LogDebug($"CreateUserAndTenant(): Linking portal user '{userInfo.Id}' to zoom user '{profile.Id}'.");
                AccountLinker.AddLink(userInfo.Id.ToString(), profile);
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

    private async Task<UserInfo> CreateUser(LoginProfile profile)
    {

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
        return await UserManagerWrapper.AddUser(userInfo, UserManagerWrapper.GeneratePassword(), afterInvite: true);
    }

    private async Task<Tenant> CreateTenant(string portalName, LoginProfile profile)
    {
        var info = new TenantRegistrationInfo
        {
            Name = portalName,
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

        if (!string.IsNullOrEmpty(ApiSystemHelper.ApiCacheUrl))
        {
            await ApiSystemHelper.AddTenantToCacheAsync(info.Address, SecurityContext.CurrentAccount.ID);
        }

        HostedSolution.RegisterTenant(info, out var tenant);

        TenantManager.SetCurrentTenant(tenant);

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
                HostedSolution.SetTariff(tenant.Id, tariff);
            }
        }

        var cspSettings = SettingsManager.Load<CspSettings>();
        cspSettings.SetDefaultIfEmpty = true;
        SettingsManager.Save(cspSettings);

        return tenant;
    }

    private string GenerateAlias(long accountNumber)
    {
        return $"zoom-{accountNumber}";
    }

    private string GetConfirmLink(long accountNumber, string uid)
    {
        ArgumentNullOrEmptyException.ThrowIfNullOrEmpty(uid, nameof(uid));

        var portalName = GenerateAlias(accountNumber);
        var tenant = HostedSolution.GetTenant(portalName);

        if (tenant == null)
        {
            return null;
        }

        TenantManager.SetCurrentTenant(tenant);

        var userId = ZoomAccountHelper.GetUserIdFromZoomUid(uid);
        if (userId == null)
        {
            return null;
        }

        var user = UserManager.GetUser(userId.Value, null);

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
        public ZoomHackController(FileStorageService<int> fileStorageService, CustomTagsService<int> tagsService, GlobalFolderHelper globalFolderHelper)
        {
        }
    }
}
