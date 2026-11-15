using Gloria.Core.Classification;
using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Ml;
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
		// Retrained models live next to the database; the base model ships with the app.
		builder.Services.AddSingleton<IAimClassifier>(services => AimClassifierFactory.Create(
			Path.Combine(DatabaseLocation.DataDirectory, "models", "user"),
			Path.Combine(AppContext.BaseDirectory, "models", "base"),
			reason => services.GetRequiredService<ILogger<IAimClassifier>>().LogWarning("Aim model not used: {Reason}", reason)));
		builder.Services.AddSingleton<UpdateService>();
		builder.Services.AddTransient<MainPage>();

		var app = builder.Build();

		DatabaseInitializer.Migrate(app.Services.GetRequiredService<IDbContextFactory<GloriaDbContext>>());

		return app;
	}
}
