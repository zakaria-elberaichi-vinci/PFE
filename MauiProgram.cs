using System.Net;
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
            // Clé de licence Syncfusion (fonctionne sur toutes les plateformes)
            SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjGyl/Vkd+XU9FcVRDQmtWfFN0Q3NYflRxfV9DZ0wgOX1dQl9mSHxTf0RiW3pfdndUR2hXUkU=");

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
            builder.Services.AddSingleton<IDatabaseService, DatabaseService>();
            builder.Services.AddSingleton<ILeaveNotificationService, LeaveNotificationService>();
            builder.Services.AddSingleton<IBackgroundNotificationService, BackgroundNotificationService>();
            builder.Services.AddSingleton<IBackgroundLeaveStatusService, BackgroundLeaveStatusService>();
            builder.Services.AddSingleton<ISyncService, SyncService>();

            builder.Services.AddSingleton<OfflineService>();
            builder.Services.AddSingleton<ViewPreferenceService>();

            builder.Services.AddTransient<AuthenticationViewModel>();
            builder.Services.AddTransient<UserProfileViewModel>();
            builder.Services.AddTransient<ManageLeavesViewModel>();
            builder.Services.AddTransient<LeaveRequestViewModel>();
            builder.Services.AddTransient<CalendarViewModel>();
            builder.Services.AddTransient<LeaveViewModel>();
            builder.Services.AddTransient<MyLeavesViewModel>();

            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<UserProfilePage>();
            builder.Services.AddTransient<DashboardPage>();
            builder.Services.AddTransient<ManageLeavesPage>();
            builder.Services.AddTransient<LeaveRequestPage>();
            builder.Services.AddTransient<CalendarPage>();
            builder.Services.AddTransient<LeavesPage>();
            builder.Services.AddTransient<MyLeavesPage>();

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