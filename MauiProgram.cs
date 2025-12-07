using System.Net;
using Microsoft.Extensions.Logging;
using PFE.Services;
using PFE.ViewModels;
using PFE.Views;

namespace PFE
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            MauiAppBuilder builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddHttpClient(nameof(OdooClient), client =>
                        {
                            client.BaseAddress = new Uri(OdooConfigService.OdooUrl);
                            client.DefaultRequestHeaders.UserAgent.ParseAdd("PFE-Client/1.0");
                        })
                            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
                            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                            {
                                UseCookies = true,
                                CookieContainer = new CookieContainer(),
                                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                                AllowAutoRedirect = true
                            });

            builder.Services.AddSingleton<OdooClient>(sp =>
            {
                IHttpClientFactory factory = sp.GetRequiredService<IHttpClientFactory>();
                HttpClient http = factory.CreateClient(nameof(OdooClient));
                return new OdooClient(http);
            });

            builder.Services.AddSingleton<AppViewModel>();
            builder.Services.AddTransient<AuthenticationViewModel>();
            builder.Services.AddTransient<UserProfileViewModel>();
            builder.Services.AddTransient<LeaveViewModel>();

            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<UserProfilePage>();
            builder.Services.AddTransient<LeavesPage>();
            builder.Services.AddTransient<DashboardPage>();

            builder.Services.AddSingleton<App>();
            builder.Services.AddTransient<Func<LoginPage>>(sp => () => sp.GetRequiredService<LoginPage>());
            builder.Services.AddTransient<ManageLeavesPage>();
#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
