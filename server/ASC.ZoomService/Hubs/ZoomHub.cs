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
using ASC.Common.Utils;
using ASC.Files.Core.ApiModels;
using ASC.Files.Core.ApiModels.RequestDto;
using ASC.Web.Files.Services.WCFService;
using ASC.ZoomService.Extensions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Distributed;

namespace ASC.ApiSystem.Hubs;

[Authorize(AuthenticationSchemes = ZoomAuthHandler.ZOOM_AUTH_SCHEME_QUERY)]
public class ZoomHub : Hub
{
    private readonly IDistributedCache _cache;
    private readonly FileStorageService _fileStorageService;
    private readonly ZoomAccountHelper _zoomAccountHelper;
    private readonly SecurityContext _securityContext;
    private readonly UserManager _userManager;
    private readonly TenantManager _tenantManager;
    private readonly TimeZoneConverter _timeZoneConverter;
    private readonly ILogger<ZoomHub> _log;

    public ZoomHub(IDistributedCache cache, FileStorageService fileStorageService, ZoomAccountHelper zoomAccountHelper,
        SecurityContext securityContext, UserManager userManager, TenantManager tenantManager, TimeZoneConverter timeZoneConverter, ILogger<ZoomHub> log)
    {
        _cache = cache;
        _fileStorageService = fileStorageService;
        _zoomAccountHelper = zoomAccountHelper;
        _securityContext = securityContext;
        _userManager = userManager;
        _tenantManager = tenantManager;
        _timeZoneConverter = timeZoneConverter;
        _log = log;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();

        var meetingId = GetMidClaim();
        if (!string.IsNullOrWhiteSpace(meetingId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupNameFromMeetingId(meetingId));
        }
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    public async Task<bool> CheckIfUser()
    {
        var userId = await _zoomAccountHelper.GetUserIdFromZoomUid(GetUidClaim());
        return !userId.HasValue || _userManager.IsUser(userId.Value);
    }

    public bool CheckCollaboration()
    {
        var meetingId = GetMidClaim();

        var cachedCollaboration = _cache.GetCollaboration(meetingId);

        return cachedCollaboration != null;
    }

    public async Task GetCollaboration()
    {
        var meetingId = GetMidClaim();

        var cachedCollaboration = _cache.GetCollaboration(meetingId);

        await Clients.Caller.SendAsync("OnCollaboration", new ZoomCollaborationRoom()
        {
            RoomId = cachedCollaboration.RoomId,
            FileId = cachedCollaboration.FileId,
            Status = cachedCollaboration.Status,
        });
    }

    public async Task<bool> CheckRights()
    {
        try
        {
            var userId = GetUidClaim();
            var meetingId = GetMidClaim();
            var cachedCollaboration = _cache.GetCollaboration(meetingId);

            if (cachedCollaboration != null && cachedCollaboration.RoomId != null)
            {
                try
                {
                    await _securityContext.AuthenticateMeWithoutCookieAsync(Core.Configuration.Constants.CoreSystem);
                    var access = cachedCollaboration.CollaborationType switch
                    {
                        ZoomCollaborationType.Edit => Files.Core.Security.FileShare.Editing,
                        _ => Files.Core.Security.FileShare.Read,
                    };

                    await _fileStorageService.SetAceObjectAsync(new AceCollection<int>()
                    {
                        Message = string.Empty,
                        Files = Array.Empty<int>(),
                        Folders = new[] { int.Parse(cachedCollaboration.RoomId) },
                        Aces = new List<AceWrapper>
                    {
                        new()
                        {
                            Id = (await _zoomAccountHelper.GetUserIdFromZoomUid(userId)).Value,
                            Access = access,
                        }
                    }
                    }, false);
                }
                finally
                {
                    _securityContext.Logout();
                }
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    public async Task CollaborateStart(string collaborationId, ZoomCollaborationChangePayload changePayload)
    {
        ArgumentException.ThrowIfNullOrEmpty(collaborationId, nameof(collaborationId));

        var meetingId = GetMidClaim();
        await Clients.Group(GetGroupNameFromMeetingId(meetingId)).SendAsync("OnCollaborationStarting");

        var uid = GetUidClaim();
        var guid = (await _zoomAccountHelper.GetUserIdFromZoomUid(uid)).Value;
        var user = _userManager.GetUsers(guid);
        var tenant = await _tenantManager.GetTenantAsync(user.TenantId);
        try
        {
            await _securityContext.AuthenticateMeWithoutCookieAsync(guid);
            var tz = TimeZoneInfo.Local;
            try
            {
                tz = _timeZoneConverter.GetTimeZone(tenant.TimeZone);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Failed to parse tenant TZ: {tenant.TimeZone}");
            }

            var room = await _fileStorageService.CreateRoomAsync($"Zoom Collaboration {TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).ToString("g", user.GetCulture())}", RoomType.CustomRoom, false, Array.Empty<FileShareParams>(), false, string.Empty);
            await CheckRights();

            var collaboration = new ZoomCollaborationCachedRoom()
            {
                ConnectionId = Context.ConnectionId,
                CollaborationId = collaborationId,
                MeetingId = meetingId,
                RoomId = room.Id.ToString(),
                FileId = null,
                CollaborationType = (ZoomCollaborationType)changePayload.CollaborationType,
                Status = ZoomCollaborationStatus.Pending,
                TenantId = user.TenantId
            };
            _cache.SetCollaboration(meetingId, collaboration);

            await Clients.Group(GetGroupNameFromMeetingId(meetingId)).SendAsync("OnCollaboration", new ZoomCollaborationRoom()
            {
                FileId = collaboration.FileId,
                RoomId = collaboration.RoomId
            });

            if (changePayload.FileId != null)
            {
                await CollaborateChange(changePayload);
            }
        }
        catch (TenantQuotaException)
        {
            await Clients.Group(GetGroupNameFromMeetingId(meetingId)).SendAsync("OnQuotaHit");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error while starting collaboration");
            throw;
        }
        finally
        {
            _securityContext.Logout();
        }
    }

    public async Task CollaborateChanging()
    {
        var meetingId = GetMidClaim();

        var cachedCollaboration = _cache.GetCollaboration(meetingId);

        ThrowIfNotCollaborationInitiator(cachedCollaboration);

        cachedCollaboration.Status = ZoomCollaborationStatus.Pending;
        cachedCollaboration.FileId = null;

        _cache.SetCollaboration(meetingId, cachedCollaboration);

        await Clients.Group(GetGroupNameFromMeetingId(meetingId)).SendAsync("OnCollaborationChanging");
    }

    public async Task CollaborateChange(ZoomCollaborationChangePayload changePayload)
    {
        ArgumentException.ThrowIfNullOrEmpty(changePayload.FileId, nameof(changePayload.FileId));

        var uid = GetUidClaim();
        var guid = (await _zoomAccountHelper.GetUserIdFromZoomUid(uid)).Value;
        try
        {
            await _securityContext.AuthenticateMeWithoutCookieAsync(guid);

            var meetingId = GetMidClaim();
            var cachedCollaboration = _cache.GetCollaboration(meetingId);

            ThrowIfNotCollaborationInitiator(cachedCollaboration);

            var roomId = int.Parse(cachedCollaboration.RoomId);
            var collabFileId = await MoveOrCreateFileIfNeeded(roomId, changePayload);

            cachedCollaboration.FileId = collabFileId.ToString();
            cachedCollaboration.Status = ZoomCollaborationStatus.InFile;
            cachedCollaboration.CollaborationType = (ZoomCollaborationType)changePayload.CollaborationType;
            _cache.SetCollaboration(meetingId, cachedCollaboration);

            await Clients.Group(GetGroupNameFromMeetingId(meetingId)).SendAsync("OnCollaboration", new ZoomCollaborationRoom()
            {
                FileId = cachedCollaboration.FileId,
                RoomId = cachedCollaboration.RoomId,
                Status = cachedCollaboration.Status
            });
        }
        catch (TenantQuotaException)
        {
            var meetingId = GetMidClaim();
            await Clients.Group(GetGroupNameFromMeetingId(meetingId)).SendAsync("OnQuotaHit");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error while changing collaboration");
            throw;
        }
        finally
        {
            _securityContext.Logout();
        }
    }

    public void CollaborateEnd()
    {
        var meetingId = GetMidClaim();
        var cachedCollaboration = _cache.GetCollaboration(meetingId);

        ThrowIfNotCollaborationInitiator(cachedCollaboration);

        //_ = Task.Run(async () =>
        //{
        //    await _zoomBackupHelper.MoveFilesToBackup(cachedCollaboration);
        //});
        _cache.RemoveCollaboration(meetingId);
    }

    private async Task<int> MoveOrCreateFileIfNeeded(int roomId, ZoomCollaborationChangePayload changePayload)
    {
        var fileId = int.Parse(changePayload.FileId);

        int collabFileId;
        if (fileId < 0)
        {
            ArgumentException.ThrowIfNullOrEmpty(changePayload.Title, nameof(changePayload.Title));
            var file = await _fileStorageService.CreateNewFileAsync(new FileModel<int, int> { ParentId = roomId, Title = changePayload.Title, TemplateId = 0 });
            collabFileId = file.Id;
        }
        else
        {
            var oldFile = await _fileStorageService.GetFileAsync(fileId, -1);
            collabFileId = oldFile.Id;
            if (oldFile.ParentId != roomId)
            {
                var file = await _fileStorageService.CreateNewFileAsync(new FileModel<int, int> { ParentId = roomId, Title = oldFile.Title, TemplateId = oldFile.Id }, true);
                collabFileId = file.Id;
            }
        }

        return collabFileId;
    }

    private string GetUidClaim()
    {
        return Context.User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_UID)?.Value;
    }

    private string GetMidClaim()
    {
        return Context.User.Claims.FirstOrDefault(c => c.Type == ZoomAuthHandler.ZOOM_CLAIM_MID)?.Value;
    }

    private void ThrowIfNotCollaborationInitiator(ZoomCollaborationCachedRoom cachedCollaboration)
    {
        if (Context.ConnectionId != cachedCollaboration.ConnectionId)
        {
            throw new UnauthorizedAccessException("Not collaboration initiator");
        }
    }

    private static string GetGroupNameFromMeetingId(string meetingId)
    {
        return $"zoom-meeting-{meetingId}";
    }
}
