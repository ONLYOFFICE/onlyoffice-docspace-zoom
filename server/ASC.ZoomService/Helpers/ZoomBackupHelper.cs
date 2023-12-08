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
using ASC.Files.Core;
using ASC.Files.Core.ApiModels;
using ASC.Files.Core.ApiModels.RequestDto;
using ASC.Files.Core.VirtualRooms;
using ASC.Web.Files.Classes;
using ASC.Web.Files.Services.WCFService;
using ASC.Web.Files.Services.WCFService.FileOperations;
using System.Text.Json;

namespace ASC.ZoomService.Helpers
{
    [Singleton]
    public class ZoomBackupHelper
    {
        private readonly ZoomAccountHelper _zoomAccountHelper;
        private readonly SecurityContext _securityContext;
        private readonly FileStorageService _fileStorageService;
        private readonly GlobalFolderHelper _globalFolderHelper;
        private readonly CustomTagsService _tagsService;
        private readonly TenantManager _tenantManager;
        private readonly ILogger<ZoomBackupHelper> _log;

        public ZoomBackupHelper(ZoomAccountHelper zoomAccountHelper, SecurityContext securityContext, FileStorageService fileStorageService,
            GlobalFolderHelper globalFolderHelper, CustomTagsService tagsService, TenantManager tenantManager, ILogger<ZoomBackupHelper> log)
        {
            _zoomAccountHelper = zoomAccountHelper;
            _securityContext = securityContext;
            _fileStorageService = fileStorageService;
            _globalFolderHelper = globalFolderHelper;
            _tagsService = tagsService;
            _tenantManager = tenantManager;
            _log = log;
        }

        public async Task MoveFilesToBackup(ZoomCollaborationCachedRoom cachedCollaboration)
        {
            try
            {
                await _tenantManager.SetCurrentTenantAsync(cachedCollaboration.TenantId);
                await _securityContext.AuthenticateMeWithoutCookieAsync((await _zoomAccountHelper.GetAdminUser()).Id);

                var parentId = await _globalFolderHelper.GetFolderVirtualRooms();
                var found = await _fileStorageService.GetFolderItemsAsync(parentId, 0, 1, FilterType.CustomRooms, false, null, null, null, false, false,
                    new OrderBy(SortedByType.DateAndTime, true), tagNames: new[] { cachedCollaboration.MeetingId });

                int? roomId = null;
                if (found.Entries.Any())
                {
                    roomId = (found.Entries.First() as Folder<int>).Id;
                }

                if (!roomId.HasValue)
                {
                    //await _tagsService.CreateTagAsync(cachedCollaboration.MeetingId);
                    var room = await _fileStorageService.CreateRoomAsync($"Zoom Meeting {DateTime.Now:MM/dd/yy}", RoomType.CustomRoom, false, Array.Empty<FileShareParams>(), false, string.Empty);
                    //await _tagsService.AddRoomTagsAsync(room.Id, new[] { cachedCollaboration.MeetingId });
                    roomId = room.Id;
                }

                var collaborationRoom = await _fileStorageService.GetFolderAsync(int.Parse(cachedCollaboration.RoomId));
                var innerRoom = await _fileStorageService.CreateNewFolderAsync(roomId.Value, collaborationRoom.Title);

                var itemsToMove = await _fileStorageService.GetFolderItemsAsync(collaborationRoom.Id, 0, 20, FilterType.None, false, null, null, null, false, false, new OrderBy(SortedByType.DateAndTime, true));
                var fileIds = itemsToMove.Entries.Where(entry => entry is File<int>).Select(entry => (entry as File<int>).Id);

                await WaitForFileOpsToComplete(await _fileStorageService.MoveOrCopyItemsAsync(
                    new List<JsonElement>(),
                    new List<JsonElement>(fileIds.Select(id => JsonSerializer.Deserialize<JsonElement>(id.ToString()))),
                    JsonSerializer.Deserialize<JsonElement>(innerRoom.Id.ToString()),
                    FileConflictResolveType.Skip,
                true));
                await WaitForFileOpsToComplete(await _fileStorageService.MoveOrCopyItemsAsync(
                    new List<JsonElement>() { JsonSerializer.Deserialize<JsonElement>(collaborationRoom.Id.ToString()) },
                    new List<JsonElement>(),
                    JsonSerializer.Deserialize<JsonElement>((await _globalFolderHelper.FolderArchiveAsync).ToString()),
                    FileConflictResolveType.Skip,
                    false));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error while moving collaboration to backup");
                throw;
            }
            finally
            {
                _securityContext.Logout();
            }
        }

        private async Task WaitForFileOpsToComplete(IEnumerable<FileOperationResult> fileOperationResults)
        {
            var idsToWait = fileOperationResults.Select(op => op.Id).ToList();

            while (idsToWait.Count > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                var newStatuses = _fileStorageService.GetTasksStatuses().ToDictionary(op => op.Id, op => op);

                foreach (var id in idsToWait.ToList())
                {
                    if (!newStatuses.TryGetValue(id, out var op) || op.Finished)
                    {
                        idsToWait.Remove(id);
                    }
                }
            }
        }
    }
}
