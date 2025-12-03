using Microsoft.Extensions.Logging;
using PFE.Services;

namespace PFE
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });
            builder.Services.AddSingleton<OdooConfigService>();
            builder.Services.AddSingleton<OdooClient>();

            builder.Services.AddTransient<LoginPage>();
            builder.Services.AddTransient<UserProfilePage>();
#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
