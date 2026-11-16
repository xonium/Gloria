using System.Globalization;
using Gloria.Core.Classification;
using Gloria.Core.Encounters;
using Gloria.Core.Weapons;

namespace Gloria.Services;

/// <summary>Turns domain values into the words and colors the screens show.</summary>
public static class Display
{
	public static readonly Color Accent = Color.FromArgb("#3987e5");
	public static readonly Color AccentSoft = Color.FromArgb("#1b2c44");
	public static readonly Color AccentSofter = Color.FromArgb("#16243a");
	public static readonly Color AccentLight = Color.FromArgb("#9cc6f5");
	public static readonly Color Loss = Color.FromArgb("#d95926");
	public static readonly Color LossSoft = Color.FromArgb("#3a2219");
	public static readonly Color LossText = Color.FromArgb("#f2b59b");
	public static readonly Color Bullet = Color.FromArgb("#eb6834");
	public static readonly Color Warn = Color.FromArgb("#fab219");
	public static readonly Color Good = Color.FromArgb("#0ca30c");
	public static readonly Color Bg = Color.FromArgb("#0f1012");
	public static readonly Color Panel = Color.FromArgb("#17191c");
	public static readonly Color Panel2 = Color.FromArgb("#1e2125");
	public static readonly Color Line = Color.FromArgb("#2b2e34");
	public static readonly Color LineStrong = Color.FromArgb("#4a4e57");
	public static readonly Color Text = Color.FromArgb("#eeeeea");
	public static readonly Color Text2 = Color.FromArgb("#b9bbc0");
	public static readonly Color Muted = Color.FromArgb("#8b8f97");

	/// <summary>
	/// The UI is in English, so numbers are too, whatever the Windows locale: "63%", "0.19 s", "1,420".
	/// The invariant culture would write "63 %", so its percent pattern is changed.
	/// </summary>
	public static CultureInfo Culture { get; } = CreateCulture();

	/// <summary>Makes every thread format with <see cref="Culture"/>. Call before any UI is created.</summary>
	public static void UseCulture()
	{
		CultureInfo.DefaultThreadCurrentCulture = Culture;
		CultureInfo.CurrentCulture = Culture;
	}

	private static CultureInfo CreateCulture()
	{
		var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
		culture.NumberFormat.PercentPositivePattern = 1;
		culture.NumberFormat.PercentNegativePattern = 1;
		return CultureInfo.ReadOnly(culture);
	}

	public static string Habit(AimCategory category) => category switch
	{
		AimCategory.Overshoot => "Overshoot",
		AimCategory.Undershoot => "Undershoot",
		AimCategory.MovingShot => "Moving shot",
		AimCategory.Hasty => "Hasty",
		AimCategory.SlowReaction => "Slow reaction",
		AimCategory.SmallFlick => "Small flick",
		AimCategory.MediumFlick => "Medium flick",
		AimCategory.LargeFlick => "Large flick",
		_ => category.ToString(),
	};

	public static string HabitDescription(AimCategory category) => category switch
	{
		AimCategory.Overshoot => "Crosshair went past the target and came back",
		AimCategory.Undershoot => "Stopped short, then several small corrections",
		AimCategory.MovingShot => "First shot above the weapon's accurate speed",
		AimCategory.Hasty => "Fired before the crosshair was on the body",
		AimCategory.SlowReaction => "First shot more than 0.6 s after sighting",
		AimCategory.SmallFlick => "Crosshair started 5–15° off the body: pre-aim a little off",
		AimCategory.MediumFlick => "Crosshair started 15–45° off the body: poor pre-aim",
		AimCategory.LargeFlick => "Crosshair started 45° or more off the body: no pre-aim",
		_ => "",
	};

	public static IReadOnlyList<string> Habits(int flags) =>
		Enum.GetValues<AimCategory>().Where(c => (flags & (1 << (int)c)) != 0).Select(Habit).ToList();

	public static string Outcome(EncounterOutcome outcome) => outcome switch
	{
		EncounterOutcome.Kill => "Kill",
		EncounterOutcome.Death => "Death",
		EncounterOutcome.Disengage => "Disengaged",
		_ => "Timed out",
	};

	public static Color OutcomeColor(EncounterOutcome outcome) => outcome switch
	{
		EncounterOutcome.Kill => Accent,
		EncounterOutcome.Death => Loss,
		_ => Muted,
	};

	public static string Date(DateTimeOffset? date) =>
		date?.ToLocalTime().ToString("d MMM yyyy", Culture) ?? "—";

	public static string ShortDate(DateTimeOffset? date) =>
		date?.ToLocalTime().ToString("d MMM", Culture) ?? "—";

	public static string Percent(int part, int whole) => whole == 0 ? "—" : $"{100.0 * part / whole:0}%";

	public static string Clock(double seconds)
	{
		var s = Math.Max(0, (int)Math.Round(seconds));
		return $"{s / 60}:{s % 60:00}";
	}

	public static string Side(char side) => side switch
	{
		'T' => "T",
		'C' => "CT",
		_ => "—",
	};

	public static string WeaponClass(WeaponClass weaponClass) => weaponClass switch
	{
		Core.Weapons.WeaponClass.Pistol => "Pistols",
		Core.Weapons.WeaponClass.Smg => "SMGs",
		Core.Weapons.WeaponClass.Rifle => "Rifles",
		Core.Weapons.WeaponClass.Sniper => "Snipers",
		Core.Weapons.WeaponClass.Shotgun => "Shotguns",
		Core.Weapons.WeaponClass.MachineGun => "Machine guns",
		_ => "Other (knife, grenade, taser)",
	};

	public static string Weapon(string weapon)
	{
		var name = StaticWeaponData.Normalize(weapon);
		return name switch
		{
			"ak47" => "AK-47",
			"m4a1" => "M4A4",
			"m4a1_silencer" => "M4A1-S",
			"awp" => "AWP",
			"ssg08" => "SSG 08",
			"deagle" => "Desert Eagle",
			"usp_silencer" => "USP-S",
			"hkp2000" => "P2000",
			"glock" => "Glock-18",
			"p250" => "P250",
			"fiveseven" => "Five-SeveN",
			"tec9" => "Tec-9",
			"cz75a" => "CZ75-Auto",
			"elite" => "Dual Berettas",
			"revolver" => "R8 Revolver",
			"galilar" => "Galil AR",
			"famas" => "FAMAS",
			"sg556" => "SG 553",
			"aug" => "AUG",
			"mp9" => "MP9",
			"mac10" => "MAC-10",
			"mp7" => "MP7",
			"mp5sd" => "MP5-SD",
			"ump45" => "UMP-45",
			"p90" => "P90",
			"bizon" => "PP-Bizon",
			"g3sg1" => "G3SG1",
			"scar20" => "SCAR-20",
			"nova" => "Nova",
			"xm1014" => "XM1014",
			"mag7" => "MAG-7",
			"sawedoff" => "Sawed-Off",
			"m249" => "M249",
			"negev" => "Negev",
			"hegrenade" => "HE grenade",
			"inferno" => "Fire",
			"molotov" => "Molotov",
			"incgrenade" => "Incendiary",
			"flashbang" => "Flashbang",
			"smokegrenade" => "Smoke",
			"decoy" => "Decoy",
			"taser" => "Zeus x27",
			"c4" or "planted_c4" => "C4",
			"world" => "World",
			"" => "—",
			_ when name.StartsWith("knife", StringComparison.OrdinalIgnoreCase) || name == "bayonet" => "Knife",
			_ => name.ToUpperInvariant(),
		};
	}

	// Files in Resources/Images/Weapons, from build/fetch-assets.ps1.
	private static readonly HashSet<string> WeaponIcons = new(StringComparer.OrdinalIgnoreCase)
	{
		"ak47", "aug", "awp", "bayonet", "bizon", "c4", "cz75a", "deagle", "decoy", "elite", "famas", "fiveseven",
		"flashbang", "g3sg1", "galilar", "glock", "hegrenade", "hkp2000", "incgrenade", "knife", "knife_butterfly",
		"knife_falchion", "knife_flip", "knife_gut", "knife_karambit", "knife_m9_bayonet", "knife_push", "knife_t",
		"knife_tactical", "m249", "m4a1", "m4a1_silencer", "mac10", "mag7", "molotov", "mp7", "mp9", "negev", "nova",
		"p250", "p90", "revolver", "sawedoff", "scar20", "sg556", "smokegrenade", "ssg08", "taser", "tec9", "ump45",
		"usp_silencer", "xm1014",
	};

	/// <summary>The icon file of a weapon (white silhouette), or null when there is none: then show only the name.</summary>
	public static string? WeaponIcon(string? weapon)
	{
		if (string.IsNullOrEmpty(weapon))
			return null;

		var name = StaticWeaponData.Normalize(weapon).ToLowerInvariant() switch
		{
			"inferno" => "molotov",
			"planted_c4" => "c4",
			var n when n.StartsWith("knife") && !WeaponIcons.Contains(n) => "knife",
			var n => n,
		};
		return WeaponIcons.Contains(name) ? $"weapon_{name}.png" : null;
	}

	/// <summary>Green when healthy, amber when hurt, red when nearly dead.</summary>
	public static Color HealthColor(int health) => health switch
	{
		> 60 => Good,
		> 25 => Warn,
		_ => Loss,
	};

	/// <summary>The game's hit group as a word.</summary>
	public static string HitGroup(int? group) => group switch
	{
		1 => "head",
		2 => "chest",
		3 => "stomach",
		4 or 5 => "arm",
		6 or 7 => "leg",
		8 => "neck",
		_ => "body",
	};
}
