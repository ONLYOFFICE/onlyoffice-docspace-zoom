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

namespace ASC.ApiSystem.Controllers;

[Scope]
public class CommonMethods
{
    private readonly CommonLinkUtility _commonLinkUtility;
    private readonly HostedSolution _hostedSolution;
    private readonly CoreBaseSettings _coreBaseSettings;
    private readonly TenantManager _tenantManager;

    public CommonMethods(
        CommonLinkUtility commonLinkUtility,
        HostedSolution hostedSolution,
        CoreBaseSettings coreBaseSettings,
        TenantManager tenantManager)
    {
        _commonLinkUtility = commonLinkUtility;
        _coreBaseSettings = coreBaseSettings;
        _tenantManager = tenantManager;
        _hostedSolution = hostedSolution;
    }

    public string CreateReference(int tenantId, string requestUriScheme, string tenantDomain, string email, bool first = false, string module = "", bool sms = false)
    {
        var url = _commonLinkUtility.GetConfirmationUrlRelative(tenantId, email, ConfirmType.Auth, (first ? "true" : "") + module + (sms ? "true" : ""));
        return $"{requestUriScheme}{Uri.SchemeDelimiter}{tenantDomain}/{url}{(first ? "&first=true" : "")}{(string.IsNullOrEmpty(module) ? "" : "&module=" + module)}{(sms ? "&sms=true" : "")}";
    }

    public async Task<(bool, Tenant)> GetTenant(IModel model)
    {
        Tenant tenant;
        if (_coreBaseSettings.Standalone && model != null && !string.IsNullOrWhiteSpace((model.PortalName ?? "")))
        {
            tenant = _tenantManager.GetTenant((model.PortalName ?? "").Trim());
            return (true, tenant);
        }

        if (model != null && model.TenantId.HasValue)
        {
            tenant = _hostedSolution.GetTenant(model.TenantId.Value);
            return (true, tenant);
        }

        if (model != null && !string.IsNullOrWhiteSpace((model.PortalName ?? "")))
        {
            tenant = await _hostedSolution.GetTenantAsync((model.PortalName ?? "").Trim());
            return (true, tenant);
        }

        tenant = null;
        return (true, tenant);
    }
}
