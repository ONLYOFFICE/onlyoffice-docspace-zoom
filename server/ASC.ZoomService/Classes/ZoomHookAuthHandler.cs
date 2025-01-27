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

using Microsoft.Extensions.Options;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;

namespace ASC.ApiSystem.Classes;

[Scope]
public class ZoomHookAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string ZOOM_HOOK_AUTH_SCHEME = "auth:zoomhook:header";

    private readonly ILogger<AuthHandler> _log;
    private readonly IConfiguration _configuration;

    public ZoomHookAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        ILogger<AuthHandler> log,
        IConfiguration configuration) :
        base(options, logger, encoder, clock)
    {
        _log = log;
        _configuration = configuration;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            string ts = null;
            string signature = null;
            if (Request.Headers.TryGetValue(ZOOM_HOOK_TS_HEADER, out var tsHeader))
            {
                ts = tsHeader.FirstOrDefault();
            }
            if (Request.Headers.TryGetValue(ZOOM_HOOK_SIGN_HEADER, out var signHeader))
            {
                signature = signHeader.FirstOrDefault();
            }

            ArgumentException.ThrowIfNullOrEmpty(ts, nameof(ts));
            ArgumentException.ThrowIfNullOrEmpty(signature, nameof(signature));

            // enable buffering so we can reuse body later
            Request.EnableBuffering();

            using var sr = new StreamReader(Request.Body, leaveOpen: true);
            var body = await sr.ReadToEndAsync();

            // rewind stream to start
            Request.Body.Seek(0, SeekOrigin.Begin);

            var hashString = $"v0:{ts}:{body}";

            using var hasher = new HMACSHA256(GetBytesFromString(_configuration["zoom:event-secret"]));
            var hash = hasher.ComputeHash(GetBytesFromString(hashString));
            var hex = Convert.ToHexString(hash);

            if (string.Equals($"v0={hex}", signature, StringComparison.InvariantCultureIgnoreCase))
            {
                _log.LogDebug($"Signature is correct");
                return AuthenticateResult.Success(new AuthenticationTicket(GetClaimsPrincipal(), new AuthenticationProperties(), Scheme.Name));
            }
            else
            {
                throw new Exception("Signature didn't match");
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Auth error; Scheme {Scheme.Name}");
            return AuthenticateResult.Fail(new AuthenticationException(nameof(HttpStatusCode.InternalServerError)));
        }
    }

    private ClaimsPrincipal GetClaimsPrincipal()
    {
        var claims = new List<Claim>();
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return new ClaimsPrincipal(identity);
    }

    private static byte[] GetBytesFromString(string str)
    {
        return Encoding.UTF8.GetBytes(str);
    }

    private const string ZOOM_HOOK_TS_HEADER = "x-zm-request-timestamp";
    private const string ZOOM_HOOK_SIGN_HEADER = "x-zm-signature";
}
