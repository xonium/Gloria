using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Updates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gloria;

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

#if DEBUG
		builder.Logging.AddDebug();
#endif

		var databasePath = DatabaseLocation.EnsureDefaultPath();
		builder.Services.AddDbContextFactory<GloriaDbContext>(options =>
			options.UseSqlite(DatabaseLocation.ConnectionString(databasePath)));
		builder.Services.AddSingleton<IDemoParser, DemoFileDemoParser>();
		builder.Services.AddSingleton<IWeaponData, StaticWeaponData>();
		builder.Services.AddSingleton<UpdateService>();
		builder.Services.AddTransient<MainPage>();

		var app = builder.Build();

		DatabaseInitializer.Migrate(app.Services.GetRequiredService<IDbContextFactory<GloriaDbContext>>());

		return app;
	}
}
