using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Data.Import;
using Gloria.Data.Queries;
using Gloria.Ml;
using Gloria.Pages;
using Gloria.Services;
using Gloria.Updates;
using Gloria.ViewModels;
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
		builder.Services.AddSingleton<DemoAnalyzer>();
		builder.Services.AddSingleton<DemoImporter>();
		builder.Services.AddSingleton(services => new GloriaReadModel(
			services.GetRequiredService<IDbContextFactory<GloriaDbContext>>(),
			services.GetRequiredService<DemoAnalyzer>().VersionKey));
		builder.Services.AddSingleton<ImportQueue>();
		builder.Services.AddSingleton<ProfileState>();
		builder.Services.AddSingleton<UpdateService>();

		builder.Services.AddSingleton<ShellViewModel>();
		builder.Services.AddSingleton<AppShell>();
		// Top-level pages and their view models live for the app's lifetime; detail pages are created per visit.
		builder.Services.AddSingleton<SetupViewModel>();
		builder.Services.AddSingleton<OverviewViewModel>();
		builder.Services.AddSingleton<MatchesViewModel>();
		builder.Services.AddSingleton<TrendsViewModel>();
		builder.Services.AddTransient<MatchViewModel>();
		builder.Services.AddTransient<EncounterViewModel>();
		builder.Services.AddTransient<WelcomePage>();
		builder.Services.AddTransient<SetupPage>();
		builder.Services.AddTransient<OverviewPage>();
		builder.Services.AddTransient<MatchesPage>();
		builder.Services.AddTransient<TrendsPage>();
		builder.Services.AddTransient<MatchPage>();
		builder.Services.AddTransient<EncounterPage>();

		var app = builder.Build();

		DatabaseInitializer.Migrate(app.Services.GetRequiredService<IDbContextFactory<GloriaDbContext>>());

		return app;
	}
}
