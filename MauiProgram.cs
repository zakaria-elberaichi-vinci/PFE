using System.Net;
using DotNetEnv;
using Microsoft.Extensions.Logging;
using PFE.Context;
using PFE.Services;
using PFE.ViewModels;
using PFE.Views;
using Plugin.LocalNotification;
using Syncfusion.Licensing;
using Syncfusion.Maui.Core.Hosting;

namespace PFE
{
    public static class MauiProgram
    {
        [Obsolete]
        public static MauiApp CreateMauiApp()
        {
            Env.Load();
            string synfusionKey = Env.GetString("SYNCFUSION_LICENSE_KEY");
            SyncfusionLicenseProvider.RegisterLicense(synfusionKey);

            MauiAppBuilder builder = MauiApp.CreateBuilder();
            builder
                .ConfigureSyncfusionCore()
                .UseMauiApp<App>()
                .UseLocalNotification()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });
            builder.Services.AddSingleton<IDatabaseService, DatabaseService>();

            builder.Services.AddSingleton(new CookieContainer());

            builder.Services.AddHttpClient(nameof(OdooClient), client =>
                        {
                            client.BaseAddress = new Uri(OdooConfigService.OdooUrl);
                            client.DefaultRequestHeaders.UserAgent.ParseAdd("PFE-Client/1.0");
                        })
                            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                            .ConfigurePrimaryHttpMessageHandler(sp => new SocketsHttpHandler
                            {
                                UseCookies = true,
                                CookieContainer = sp.GetRequiredService<CookieContainer>(),
                                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                                AllowAutoRedirect = true
                            });

            builder.Services.AddSingleton<OdooClient>(sp =>
            {
                IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
                HttpClient http = factory.CreateClient(nameof(OdooClient));
                SessionContext session = sp.GetRequiredService<SessionContext>();
                CookieContainer cookies = sp.GetRequiredService<CookieContainer>();
                return new OdooClient(http, session, cookies);
            });

            builder.Services.AddSingleton<ILeaveNotificationService, LeaveNotificationService>();
            builder.Services.AddSingleton<IBackgroundNotificationService, BackgroundNotificationService>();
            builder.Services.AddSingleton<IBackgroundLeaveStatusService, BackgroundLeaveStatusService>();

            builder.Services.AddTransient<AuthenticationViewModel>();
            builder.Services.AddTransient<UserProfileViewModel>();
            builder.Services.AddTransient<ManageLeavesViewModel>();
            builder.Services.AddTransient<LeaveRequestViewModel>();
            builder.Services.AddTransient<CalendarViewModel>();

            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<UserProfilePage>();
            builder.Services.AddTransient<DashboardPage>();
            builder.Services.AddTransient<ManageLeavesPage>();
            builder.Services.AddTransient<LeaveRequestPage>();
            builder.Services.AddTransient<CalendarPage>();

            builder.Services.AddSingleton<App>(sp => new App(sp));
            builder.Services.AddTransient<Func<LoginPage>>(sp => () => sp.GetRequiredService<LoginPage>());

            builder.Services.AddSingleton<SessionContext>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}