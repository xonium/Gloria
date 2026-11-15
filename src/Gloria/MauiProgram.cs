using Gloria.Core.Analysis;
using Gloria.Core.Classification;
using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;
using Gloria.Data;
using Gloria.Data.Corrections;
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
		Display.UseCulture();
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
		var userModels = Path.Combine(DatabaseLocation.DataDirectory, "models", "user");
		var baseModels = Path.Combine(AppContext.BaseDirectory, "models", "base");
		builder.Services.AddDbContextFactory<GloriaDbContext>(options =>
			options.UseSqlite(DatabaseLocation.ConnectionString(databasePath)));
		builder.Services.AddSingleton<IDemoParser, DemoFileDemoParser>();
		builder.Services.AddSingleton<IWeaponData, StaticWeaponData>();
		// Retrained models live next to the database; the base model ships with the app.
		// One switchable classifier, so a retrained user model replaces it everywhere at once.
		var startedWithUserModel = false;
		builder.Services.AddSingleton(services => new SwitchableAimClassifier(AimClassifierFactory.Create(
			userModels,
			baseModels,
			reason => services.GetRequiredService<ILogger<IAimClassifier>>().LogWarning("Aim model not used: {Reason}", reason),
			directory => startedWithUserModel = directory != baseModels)));
		builder.Services.AddSingleton<IAimClassifier>(services => services.GetRequiredService<SwitchableAimClassifier>());
		builder.Services.AddSingleton<CorrectionStore>();
		builder.Services.AddSingleton(services =>
		{
			// Resolving the classifier first sets startedWithUserModel.
			var classifier = services.GetRequiredService<SwitchableAimClassifier>();
			return new UserModelTraining(
				services.GetRequiredService<CorrectionStore>(),
				classifier,
				services.GetRequiredService<AppSettings>(),
				userModels,
				startedWithUserModel,
				services.GetRequiredService<ILogger<UserModelTraining>>());
		});
		builder.Services.AddSingleton(services => new DuelOutcomes(OutcomePredictorFactory.Create(
			userModels,
			baseModels,
			reason => services.GetRequiredService<ILogger<DuelOutcomes>>().LogWarning("Outcome model not used: {Reason}", reason))));
		builder.Services.AddSingleton<AppSettings>();
		builder.Services.AddSingleton<DemoAnalyzer>();
		builder.Services.AddSingleton<DemoImporter>();
		builder.Services.AddSingleton(services => new GloriaReadModel(
			services.GetRequiredService<IDbContextFactory<GloriaDbContext>>(),
			services.GetRequiredService<DemoImporter>().AnalysisVersionKey));
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
