// Copyright (C) 2026 MaNGOS Database Studio contributors
// SPDX-License-Identifier: AGPL-3.0-or-later
// Licensed under the GNU Affero General Public License v3.0 or later; see LICENSE.

// Application startup, dependency injection, and desktop hosting.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using MangosDatabaseStudio.App.Desktop;
using MangosDatabaseStudio.App.Services;
using MangosDatabaseStudio.App.Services.FieldData;
using MangosDatabaseStudio.Application.Connections;
using MangosDatabaseStudio.Application.DatabaseUpdates;
using MangosDatabaseStudio.Application.Schema;
using MangosDatabaseStudio.Domain.Security;
using MangosDatabaseStudio.Infrastructure.MySql;
using MangosDatabaseStudio.Schema;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Photino.NET;

namespace MangosDatabaseStudio.App
{
    internal static class Program
    {
        [STAThread]
        // Enter the desktop runtime and report startup failures without leaving a child server running.
        private static int Main(string[] args)
        {
            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return 0;
            }
            catch (DllNotFoundException error)
            {
                TryWriteError("MaNGOS Database Studio could not load its native window dependencies. On Linux, install GTK 3 and a compatible WebKitGTK runtime. " + error.Message);
                return 2;
            }
            catch (Exception error)
            {
                TryWriteError("MaNGOS Database Studio failed: " + error.Message);
                return 1;
            }
        }

        // Configure the local application host and start the native desktop window.
        private static async Task<int> RunAsync(string[] args)
        {
            DesktopOptions options = DesktopOptions.Parse(args);
            if (options.ShowHelp)
            {
                DesktopOptions.PrintUsage();
                return 0;
            }

            DesktopPrivilegeGuard.EnsureMainProcessIsUnprivileged();
            DesktopDeviceIdentity deviceIdentity = options.DeviceIdentityEnabled
                ? DesktopDeviceIdentityProvider.Collect()
                : DesktopDeviceIdentity.Disabled();

            ConfigureDesktopEnvironment(deviceIdentity);
            ConfigureDatabaseClientPaths();

            int privatePort = SelectPrivatePort(options.PreferredPort);
            DesktopSessionGate sessionGate = new(privatePort);
            DesktopStartupState startupState = new();
            string contentRoot = ResolveContentRoot();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = contentRoot,
                EnvironmentName = options.EnvironmentName
            });

            // The web stack is an implementation detail of the desktop window. Keep Kestrel
            // silent and bind it exclusively to IPv4 loopback; it is not a user-facing server.
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(serverOptions =>
            {
                serverOptions.AddServerHeader = false;
                serverOptions.Listen(IPAddress.Loopback, privatePort);
            });

            string dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
            PrepareDataDirectories(dataDirectory);

            builder.Services.AddSingleton(sessionGate);
            builder.Services.AddSingleton(startupState);
            builder.Services.AddControllersWithViews();
            builder.Services.Configure<RouteOptions>(routeOptions =>
            {
                routeOptions.LowercaseUrls = true;
                routeOptions.LowercaseQueryStrings = false;
            });
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddDistributedMemoryCache();
            builder.Services.AddSession(sessionOptions =>
            {
                sessionOptions.Cookie.Name = ".MangosDatabaseStudio.Session";
                sessionOptions.Cookie.HttpOnly = true;
                sessionOptions.Cookie.IsEssential = true;
                sessionOptions.Cookie.SameSite = SameSiteMode.Strict;
                sessionOptions.IdleTimeout = TimeSpan.FromHours(8);
            });
            builder.Services.AddDataProtection()
                .SetApplicationName("MangosDatabaseStudio")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));
            builder.Services.Configure<FormOptions>(formOptions => formOptions.MultipartBodyLengthLimit = 512L * 1024L * 1024L);
            builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(cookieOptions =>
                {
                    cookieOptions.LoginPath = "/account/login";
                    cookieOptions.AccessDeniedPath = "/account/access-denied";
                    cookieOptions.Cookie.Name = ".MangosDatabaseStudio.Auth";
                    cookieOptions.Cookie.HttpOnly = true;
                    cookieOptions.Cookie.SameSite = SameSiteMode.Strict;
                    cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(12);
                    cookieOptions.SlidingExpiration = true;
                    cookieOptions.Events.OnValidatePrincipal = async context =>
                    {
                        string? idValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (!Guid.TryParse(idValue, out Guid userId))
                        {
                            context.RejectPrincipal();
                            return;
                        }

                        IApplicationUserStore userStore = context.HttpContext.RequestServices.GetRequiredService<IApplicationUserStore>();
                        ApplicationUser? storedUser = await userStore.GetAsync(userId, context.HttpContext.RequestAborted);
                        if (storedUser == null)
                        {
                            context.RejectPrincipal();
                            return;
                        }

                        IRolePermissionStore rolePermissions = context.HttpContext.RequestServices.GetRequiredService<IRolePermissionStore>();
                        IReadOnlyCollection<string> permissions = await rolePermissions.GetPermissionsAsync(storedUser.Role, context.HttpContext.RequestAborted);
                        if (!ApplicationPermissionClaims.Matches(context.Principal, storedUser, permissions))
                        {
                            context.ReplacePrincipal(ApplicationPermissionClaims.CreatePrincipal(storedUser, permissions));
                            context.ShouldRenew = true;
                        }
                    };
                });
            builder.Services.AddAuthorization(authorizationOptions =>
            {
                foreach (AppPermissionDefinition permission in AppPermissions.All)
                {
                    authorizationOptions.AddPolicy(permission.Name, policy =>
                    {
                        policy.RequireClaim(ApplicationPermissionClaims.PermissionClaimType, permission.Name);
                        if (permission.AdministratorOnly)
                        {
                            policy.RequireRole(AppRoles.Administrator);
                        }
                    });
                }
            });

            builder.Services.AddSingleton(ApplicationVersionInfo.FromAssembly(typeof(ApplicationVersionInfo).Assembly));
            builder.Services.AddSingleton<OperationProgressStore>();
            builder.Services.AddScoped<OperationProgressReporter>();
            builder.Services.AddSingleton<ApplicationDatabase>();
            builder.Services.AddSingleton<IMangosFieldDataCatalog, SqliteMangosFieldDataCatalog>();
            builder.Services.AddSingleton<IFieldDataDefinitionStore, SqliteFieldDataDefinitionStore>();
            builder.Services.AddSingleton<IApplicationUserStore, SqliteApplicationUserStore>();
            builder.Services.AddSingleton<IRolePermissionStore, SqliteRolePermissionStore>();
            builder.Services.AddSingleton<IUserAvatarStorage, UserAvatarStorage>();
            builder.Services.AddSingleton<IDesktopDeviceIdentityAccessor, EnvironmentDesktopDeviceIdentityAccessor>();
            builder.Services.AddSingleton<IConnectionProfileStore, SqliteConnectionProfileStore>();
            builder.Services.AddSingleton<IMangosDatabaseUpdateSourceStore, SqliteMangosDatabaseUpdateSourceStore>();
            builder.Services.AddSingleton<IChangeStorageStore, SqliteChangeStorageStore>();
            builder.Services.AddScoped<IActiveConnectionAccessor, SessionActiveConnectionAccessor>();
            builder.Services.AddMangosMySqlInfrastructure(builder.Configuration);
            builder.Services.AddMangosSchema();

            await using WebApplication app = builder.Build();

            ConfigureDesktopRequestPipeline(app, sessionGate, startupState);
            MapDesktopStartupEndpoints(app, startupState);
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            using CancellationTokenSource cancellation = new();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            await app.StartAsync(cancellation.Token).ConfigureAwait(false);
            Task initialization = InitializeApplicationAsync(app, startupState, cancellation.Token);

            Uri baseAddress = new UriBuilder("http", "127.0.0.1", privatePort).Uri;
            string bootstrapAddress = new Uri(baseAddress,
                DesktopSessionGate.BootstrapPath.TrimStart('/') + "?token=" + Uri.EscapeDataString(sessionGate.BootstrapToken)).ToString();

            PhotinoWindow window = new PhotinoWindow()
                .SetTitle("MaNGOS Database Studio")
                .SetUseOsDefaultSize(false)
                .SetSize(new System.Drawing.Size(1280, 800))
                .Center()
                .SetResizable(true)
                .Load(bootstrapAddress);

            window.WaitForClose();
            cancellation.Cancel();

            try
            {
                await initialization.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            await app.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            return 0;
        }

        // Configure desktop request pipeline according to the available model or service contract.
        private static void ConfigureDesktopRequestPipeline(
            WebApplication app,
            DesktopSessionGate sessionGate,
            DesktopStartupState startupState)
        {
            app.Use(async (context, next) =>
            {
                if (context.Request.Path.Equals(new PathString(DesktopSessionGate.BootstrapPath)))
                {
                    if (!sessionGate.TryBootstrap(context))
                    {
                        await DesktopSessionGate.RejectAsync(context).ConfigureAwait(false);
                        return;
                    }

                    context.Response.Redirect(DesktopSessionGate.StartupPath);
                    return;
                }

                if (!sessionGate.IsAuthorized(context))
                {
                    await DesktopSessionGate.RejectAsync(context).ConfigureAwait(false);
                    return;
                }

                bool startupEndpoint = context.Request.Path.Equals(new PathString(DesktopSessionGate.StartupPath))
                    || context.Request.Path.Equals(new PathString(DesktopSessionGate.StatusPath));
                if (!startupEndpoint && !startupState.IsReady)
                {
                    // On failure, return the recovery screen only for navigational requests.
                    // API requests are rejected with 503; no controller executes before startup.
                    if (context.Request.Method == HttpMethods.Get
                        && context.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.Redirect(DesktopSessionGate.StartupPath);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    }

                    return;
                }

                await next().ConfigureAwait(false);
            });

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/home/error");
            }

            app.UseStaticFiles();
            app.UseRouting();
            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();
        }

        // Map desktop startup endpoints according to the available model or service contract.
        private static void MapDesktopStartupEndpoints(WebApplication app, DesktopStartupState startupState)
        {
            app.MapGet(DesktopSessionGate.StartupPath, async context =>
            {
                context.Response.Headers["Cache-Control"] = "no-store";
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(DesktopStartupPage.Html, context.RequestAborted).ConfigureAwait(false);
            });

            app.MapGet(DesktopSessionGate.StatusPath, async context =>
            {
                DesktopStartupSnapshot snapshot = startupState.GetSnapshot();
                context.Response.Headers["Cache-Control"] = "no-store";
                await context.Response.WriteAsJsonAsync(new
                {
                    ready = snapshot.IsReady,
                    failed = snapshot.HasFailed,
                    progress = snapshot.Progress,
                    status = snapshot.Status,
                    detail = snapshot.Detail,
                    message = snapshot.HasFailed ? snapshot.ErrorMessage : null,
                    failureArea = snapshot.FailureArea,
                    recoverySteps = snapshot.RecoverySteps,
                    fieldData = new
                    {
                        visible = snapshot.FieldDataVisible,
                        completed = snapshot.FieldDataCompleted,
                        total = snapshot.FieldDataTotal,
                        current = snapshot.FieldDataCurrent
                    }
                }, context.RequestAborted).ConfigureAwait(false);
            });
        }

        // Initialize application according to the available model or service contract.
        private static async Task InitializeApplicationAsync(
            WebApplication app,
            DesktopStartupState startupState,
            CancellationToken cancellationToken)
        {
            try
            {
                startupState.Report(5, "Starting desktop services", "Bringing the private application host and desktop workspace online...");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);

                startupState.Report(8, "Starting secure workspace", "Preparing local application services and encrypted storage...");
                await app.Services.GetRequiredService<ApplicationDatabase>()
                    .InitializeAsync(cancellationToken, startupState.Report, startupState.ReportFieldData)
                    .ConfigureAwait(false);
                startupState.MarkReady();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                startupState.MarkFailed(exception);
            }
        }

        // Handle prepare data directories within the program workflow.
        private static void PrepareDataDirectories(string dataDirectory)
        {
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(Path.Combine(dataDirectory, "imports"));
            Directory.CreateDirectory(Path.Combine(dataDirectory, "exports"));
            Directory.CreateDirectory(Path.Combine(dataDirectory, "database"));
            Directory.CreateDirectory(Path.Combine(dataDirectory, "keys"));
            Directory.CreateDirectory(Path.Combine(dataDirectory, "avatars"));
        }

        // Select private port according to the available model or service contract.
        private static int SelectPrivatePort(int? preferredPort)
        {
            if (preferredPort.HasValue && IsPortAvailable(preferredPort.Value))
            {
                return preferredPort.Value;
            }

            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        // Determine whether port available according to the available model or service contract.
        private static bool IsPortAvailable(int port)
        {
            TcpListener listener = new(IPAddress.Loopback, port);
            try
            {
                listener.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                listener.Stop();
            }
        }

        // Resolve content root according to the available model or service contract.
        private static string ResolveContentRoot()
        {
            string? configured = Environment.GetEnvironmentVariable("MDS_CONTENT_ROOT");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")))
            {
                return AppContext.BaseDirectory;
            }

            string current = Environment.CurrentDirectory;
            if (Directory.Exists(Path.Combine(current, "wwwroot"))
                && Directory.Exists(Path.Combine(current, "Views")))
            {
                return current;
            }

            DirectoryInfo? directory = new(current);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, "src", "MangosDatabaseStudio.App");
                if (File.Exists(Path.Combine(candidate, "MangosDatabaseStudio.App.csproj")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return AppContext.BaseDirectory;
        }

        // Configure desktop environment according to the available model or service contract.
        private static void ConfigureDesktopEnvironment(DesktopDeviceIdentity deviceIdentity)
        {
            Environment.SetEnvironmentVariable("MDS_DEVICE_IDENTITY_ENABLED", deviceIdentity.IsEnabled ? "1" : "0");
            Environment.SetEnvironmentVariable("MDS_DEVICE_AVAILABLE", deviceIdentity.IsAvailable ? "1" : "0");
            SetEnvironmentValue("MDS_DEVICE_IDENTIFIER", deviceIdentity.Identifier);
            SetEnvironmentValue("MDS_DEVICE_IDENTIFIER_SOURCE", deviceIdentity.Source);
            SetEnvironmentValue("MDS_DEVICE_PLATFORM", deviceIdentity.Platform);
            SetEnvironmentValue("MDS_DEVICE_MANUFACTURER", deviceIdentity.Manufacturer);
            SetEnvironmentValue("MDS_DEVICE_MODEL", deviceIdentity.Model);
            SetEnvironmentValue("MDS_DEVICE_STATUS", deviceIdentity.StatusMessage);
            Environment.SetEnvironmentVariable("MDS_DEVICE_COLLECTED_AT_UTC", deviceIdentity.CollectedAtUtc.ToString("O"));
        }

        internal static readonly string[] string_array_executable = new[] { "mysql.exe", "mariadb.exe" };
        internal static readonly string[] string_array_naming = new[] { "mysql", "mariadb" };

        // Configure database client paths according to the available model or service contract.
        private static void ConfigureDatabaseClientPaths()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MySql__ImportClientPath")))
            {
                string? importClient = FindExecutable(OperatingSystem.IsWindows()
                    ? string_array_executable : string_array_naming);
                SetEnvironmentValue("MySql__ImportClientPath", importClient);
            }

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MySql__ExportClientPath")))
            {
                string? exportClient = FindExecutable(OperatingSystem.IsWindows()
                    ? string_array_executable : string_array_naming);
                SetEnvironmentValue("MySql__ExportClientPath", exportClient);
            }
        }

        // Locate executable according to the available model or service contract.
        private static string? FindExecutable(string[] candidates)
        {
            string? pathValue = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrWhiteSpace(pathValue))
            {
                return null;
            }

            foreach (string directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string candidate in candidates)
                {
                    try
                    {
                        string fullPath = Path.Combine(directory.Trim(), candidate);
                        if (File.Exists(fullPath))
                        {
                            return fullPath;
                        }
                    }
                    catch (Exception) when (directory.Length > 0)
                    {
                    }
                }
            }

            return null;
        }

        // Set environment value according to the available model or service contract.
        private static void SetEnvironmentValue(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        // Attempt to write error according to the available model or service contract.
        private static void TryWriteError(string message)
        {
            try
            {
                Console.Error.WriteLine(message);
            }
            catch
            {
            }
        }
    }
}
