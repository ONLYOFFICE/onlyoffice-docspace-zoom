// (c) Copyright Ascensio System SIA 2025
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

using ASC.Notify.Patterns;
using ASC.Web.Core.Notify;
using ASC.Web.Studio.Core.Notify;

namespace ASC.ApiSystem.Services.NotifyService;

[Scope]
public class NotifyClient
{
    private readonly NotifySource _notifySource;
    private readonly StudioNotifyHelper _studioNotifyHelper;
    private readonly WorkContext _notifyContext;
    private readonly IServiceProvider _serviceProvider;

    public NotifyClient(
        WorkContext notifyContext,
        NotifySource notifySource,
        StudioNotifyHelper studioNotifyHelper,
        IServiceProvider serviceProvider)
    {
        _notifyContext = notifyContext;
        _notifySource = notifySource;
        _studioNotifyHelper = studioNotifyHelper;
        _serviceProvider = serviceProvider;
    }

    public async Task SendZoomWelcome(Guid userId, string firstName, CultureInfo culture)
    {
        var client = _notifyContext.RegisterClient(_serviceProvider, _notifySource);
        var recipient = await _notifySource.GetRecipientsProvider().GetRecipientAsync(userId.ToString());

        await client.SendNoticeToAsync(NotifyConstants.ZoomWelcome,
            new[] { recipient },
            new[] { "email.sender" },
            new TagValue(CommonTags.Culture, culture.Name),
            new TagValue(CommonTags.TopGif, _studioNotifyHelper.GetNotificationImageUrl("welcome.gif")),
            new TagValue(Tags.UserName, firstName.HtmlEncode()),
            TagValues.TrulyYours(_studioNotifyHelper, WebstudioNotifyPatternResource.ResourceManager.GetString("TrulyYoursText", culture)));
    }
}
