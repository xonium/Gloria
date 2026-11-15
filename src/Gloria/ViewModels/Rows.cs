using System.Windows.Input;
using Gloria.Data.Queries;
using Gloria.Services;

namespace Gloria.ViewModels;

/// <summary>One match in a list, already formatted.</summary>
public sealed record MatchRow(
	string Date,
	string Map,
	string Result,
	Color ResultBackground,
	Color ResultForeground,
	string Score,
	string Side,
	string Encounters,
	string Duels,
	string FirstShot,
	string Habit,
	string Status,
	Color StatusColor,
	ICommand Open)
{
	public static MatchRow From(MatchSummary m)
	{
		var won = m.RoundsWon > m.RoundsLost;
		var tie = m.RoundsWon == m.RoundsLost;
		return new MatchRow(
			Display.ShortDate(m.PlayedAt),
			m.MapName,
			tie ? "D" : won ? "W" : "L",
			tie ? Display.Panel2 : won ? Display.AccentSoft : Display.LossSoft,
			tie ? Display.Text2 : won ? Display.AccentLight : Display.LossText,
			$"{m.RoundsWon}–{m.RoundsLost}",
			Display.Side(m.StartingSide),
			m.Encounters.ToString(),
			$"{m.Kills} / {m.Kills + m.Deaths}",
			Display.Percent(m.FirstShotHits, m.FirstShotKnown),
			m.MainHabit is { } h ? Display.Habit(h) : "—",
			m.IsCurrentVersion ? "Current" : "Older version",
			m.IsCurrentVersion ? Display.Good : Display.Warn,
			new Command(async () => await Shell.Current.GoToAsync($"match?demo={m.DemoId}")));
	}
}

/// <summary>A habit tag on an encounter.</summary>
public sealed record TagItem(string Name, Color Background, Color Foreground, Color Dot)
{
	public static IReadOnlyList<TagItem> For(int flags)
	{
		var habits = Display.Habits(flags);
		return habits.Count == 0
			? [new TagItem("Clean", Color.FromArgb("#15261a"), Color.FromArgb("#a8dca8"), Display.Good)]
			: habits.Select(h => new TagItem(h, Color.FromArgb("#2a2418"), Color.FromArgb("#f3d9a4"), Display.Warn)).ToList();
	}
}

/// <summary>One encounter in a list, already formatted.</summary>
public sealed record EncounterItem(
	string At,
	string Where,
	string Opponent,
	string Weapons,
	string Outcome,
	Color OutcomeColor,
	string FirstShot,
	string Error,
	string Speed,
	IReadOnlyList<TagItem> Tags,
	ICommand Open)
{
	public static EncounterItem From(EncounterRow e) => new(
		Display.Clock(e.SecondsIntoRound),
		$"{e.MapName} · round {e.RoundNumber} · vs {e.OpponentName}",
		e.OpponentName,
		$"{Display.Weapon(e.Weapon)} vs {Display.Weapon(e.OpponentWeapon)}",
		$"{Display.Outcome(e.Outcome)} · {e.DurationSeconds:0.00} s",
		Display.OutcomeColor(e.Outcome),
		e.TimeToFirstShot is { } t ? $"{t:0.00} s" : "—",
		$"{e.ErrorAtFirstShot:0.0}°",
		$"{e.SpeedRatio:0.00}×",
		TagItem.For(e.Flags),
		new Command(async () => await Shell.Current.GoToAsync($"encounter?id={e.Id}")));
}
