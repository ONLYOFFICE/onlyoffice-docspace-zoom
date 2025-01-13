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

using ASC.Core.Common.Configuration;
using ASC.FederatedLogin.LoginProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Security.Authentication;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ASC.ApiSystem.Classes;

[Scope]
public class ZoomAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string ZOOM_AUTH_SCHEME_HEADER = "auth:zoom:header";
    public const string ZOOM_AUTH_SCHEME_QUERY = "auth:zoom:query";
    public const string ZOOM_CLAIM_UID = "zoom_uid";
    public const string ZOOM_CLAIM_MID = "zoom_mid";

    private readonly ILogger<AuthHandler> _log;

    private readonly ZoomLoginProvider _zoomLoginProvider;

    public ZoomAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ISystemClock clock,
        ILogger<AuthHandler> log,
        ConsumerFactory consumerFactory) :
        base(options, logger, encoder, clock)
    {
        _log = log;

        _zoomLoginProvider = consumerFactory.Get<ZoomLoginProvider>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            var zoomContextString = string.Empty;
            switch (Scheme.Name)
            {
                case ZOOM_AUTH_SCHEME_HEADER:
                    if (Request.Headers.TryGetValue(ZOOM_CONTEXT_HEADER, out var zoomHeader))
                    {
                        zoomContextString = zoomHeader.First();
                    }
                    break;

                case ZOOM_AUTH_SCHEME_QUERY:
                    if (Request.Query.TryGetValue(ZOOM_CONTEXT_QUERY, out var zoomQuery))
                    {
                        zoomContextString = zoomQuery.First();
                    }
                    break;
            }

            if (string.IsNullOrWhiteSpace(zoomContextString))
            {
                _log.LogDebug($"Missing Zoom security context; Scheme {Scheme.Name}");
                return Task.FromResult(AuthenticateResult.Fail(new AuthenticationException(nameof(HttpStatusCode.Unauthorized))));
            }

            var contextJson = GetContext(zoomContextString);
            var context = JsonSerializer.Deserialize<ZoomHeaderContextModel>(contextJson, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true });
            _log.LogDebug($"Zoom security context validated; Scheme {Scheme.Name}; Context {contextJson};");

            var expiryDate = DateTimeOffset.FromUnixTimeMilliseconds(context.Exp);
            var now = DateTimeOffset.UtcNow;
            if (now >= expiryDate)
            {
                _log.LogDebug($"Zoom security context is expired; Issued at {DateTimeOffset.FromUnixTimeMilliseconds(context.Ts)}, Expired at {expiryDate}, Now {now}");
                throw new Exception("Zoom security context is expired");
            }

            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(GetClaimsPrincipal(context), new AuthenticationProperties(), Scheme.Name)));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, $"Auth error; Scheme {Scheme.Name}");
            return Task.FromResult(AuthenticateResult.Fail(new AuthenticationException(nameof(HttpStatusCode.InternalServerError))));
        }
    }

    private ClaimsPrincipal GetClaimsPrincipal(ZoomHeaderContextModel zoomContext)
    {
        var claims = new List<Claim>
        {
            new Claim(ZOOM_CLAIM_UID, zoomContext.Uid),
        };

        if (!string.IsNullOrEmpty(zoomContext.Mid))
        {
            claims.Add(new Claim(ZOOM_CLAIM_MID, zoomContext.Mid));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        return new ClaimsPrincipal(identity);
    }

    private string GetContext(string headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            throw new ArgumentException("zoom header must be a valid, not-empty string", nameof(headerValue));
        }

        if (string.IsNullOrWhiteSpace(_zoomLoginProvider.ClientSecret))
        {
            throw new Exception("zoom secret is null");
        }

        return ValidatePayload(headerValue);
    }

    private string ValidatePayload(string headerValue)
    {
        var payload = Unpack(headerValue);
        using var decryptor = new AesGcm(SHA256.HashData(Encoding.UTF8.GetBytes(_zoomLoginProvider.ClientSecret)));
        var output = new byte[payload.Cipher.Length];
        decryptor.Decrypt(payload.IV, payload.Cipher, payload.Tag, output, payload.AAD);
        var json = Encoding.UTF8.GetString(output);
        return json;
    }

    private static UnpackedZoomHeader Unpack(string headerValue)
    {
        var bytes = Base64UrlEncoder.DecodeBytes(headerValue);
        using var stream = new MemoryStream(bytes.Length);
        stream.Write(bytes);
        stream.Seek(0, SeekOrigin.Begin);
        using var reader = new BinaryReader(stream);

        var ivLength = reader.ReadSByte();
        var iv = reader.ReadBytes(ivLength);

        var aadLength = reader.ReadUInt16();
        var aad = reader.ReadBytes(aadLength);

        var cipherLength = reader.ReadInt32();
        var cipher = reader.ReadBytes(cipherLength);

        var tag = reader.ReadBytes(16);

        return new UnpackedZoomHeader()
        {
            IV = iv,
            AAD = aad,
            Cipher = cipher,
            Tag = tag,
        };
    }

    private class UnpackedZoomHeader
    {
        public byte[] IV { get; set; }
        public byte[] AAD { get; set; }
        public byte[] Cipher { get; set; }
        public byte[] Tag { get; set; }
    }

    private const string ZOOM_CONTEXT_HEADER = "x-zoom-app-context";
    private const string ZOOM_CONTEXT_QUERY = "zoomAppContextToken";
}
