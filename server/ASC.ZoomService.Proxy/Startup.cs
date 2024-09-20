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

using ASC.ApiSystem.Hubs;
using ASC.Core.Common.Notify.Engine;
using ASC.Core.Common.Quota;
using ASC.Core.Common.Quota.Features;
using ASC.Core.Notify.Socket;
using ASC.Files.Core.Core;
using ASC.Files.Core.EF;
using ASC.Notify.Engine;
using ASC.Notify.Textile;
using ASC.Web.Files;
using ASC.Web.Studio.Core.Notify;
using ASC.ZoomService.Middlewares;
using System.Threading.Channels;

namespace ASC.ZoomService.Proxy;

public class Startup
{
    private const string CustomCorsPolicyName = "Basic";
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly DIHelper _diHelper;
    private readonly string _corsOrigin;

    public Startup(IConfiguration configuration, IHostEnvironment hostEnvironment)
    {
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _diHelper = new DIHelper();
        _corsOrigin = _configuration["core:cors"];
    }

    public async Task ConfigureServices(IServiceCollection services)
    {
        services.AddCustomHealthCheck(_configuration);
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddHttpClient();

        services.AddSession();

        _diHelper.Configure(services);
        _diHelper.Scan();

        Action<JsonOptions> jsonOptions = options =>
        {
            options.JsonSerializerOptions.WriteIndented = false;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.Converters.Add(new ApiDateTimeConverter());
        };

        services.AddControllers()
            .AddXmlSerializerFormatters()
            .AddJsonOptions(jsonOptions);

        services.AddSingleton(jsonOptions);

        if (!string.IsNullOrEmpty(_corsOrigin))
        {
            services.AddCors(options =>
            {
                options.AddPolicy(name: CustomCorsPolicyName,
                                  policy =>
                                  {
                                      policy.WithOrigins(_corsOrigin)
                                      .SetIsOriginAllowedToAllowWildcardSubdomains()
                                      .AllowAnyHeader()
                                      .AllowAnyMethod()
                                      .AllowCredentials();
                                  });
            });
        }

        var connectionMultiplexer = await services.GetRedisConnectionMultiplexerAsync(_configuration, GetType().Namespace);

        services.AddDistributedCache(connectionMultiplexer);
        services.AddEventBus(_configuration);
        services.AddDistributedTaskQueue();
        services.AddCacheNotify(_configuration);
        services.AddDistributedLock(_configuration);

        services.RegisterFeature();

        services.AddAutoMapper(BaseStartup.GetAutoMapperProfileAssemblies());

        if (!_hostEnvironment.IsDevelopment())
        {
            services.AddStartupTask<WarmupServicesStartupTask>()
                    .TryAddSingleton(services);
        }

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AuthHandler>("auth:allowskip:default", _ => { })
            .AddScheme<AuthenticationSchemeOptions, AuthHandler>("auth:allowskip:registerportal", _ => { })
            .AddScheme<AuthenticationSchemeOptions, ZoomAuthHandler>(ZoomAuthHandler.ZOOM_AUTH_SCHEME_HEADER, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ZoomAuthHandler>(ZoomAuthHandler.ZOOM_AUTH_SCHEME_QUERY, _ => { })
            .AddScheme<AuthenticationSchemeOptions, ZoomHookAuthHandler>(ZoomHookAuthHandler.ZOOM_HOOK_AUTH_SCHEME, _ => { });

        services.AddExceptionHandler<ZoomExceptionHandler>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.UseRouting();

        if (!string.IsNullOrEmpty(_corsOrigin))
        {
            app.UseCors(CustomCorsPolicyName);
        }

        app.UseSynchronizationContextMiddleware();

        app.UseAuthentication();

        app.UseAuthorization();

        app.UseCultureMiddleware();

        app.ApplicationServices.GetRequiredService<NotifyConfiguration>().Configure();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapCustomAsync().Wait();

            endpoints.MapHealthChecks("/health", new HealthCheckOptions()
            {
                Predicate = _ => true,
                ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
            });

            endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
            {
                Predicate = r => r.Name.Contains("self")
            });
        });
    }
}