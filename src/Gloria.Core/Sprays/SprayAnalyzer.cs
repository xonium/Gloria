using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Weapons;

namespace Gloria.Core.Sprays;

/// <summary>One player's bullets at one position of their sprays with one gun, in one demo.</summary>
/// <param name="Weapon">Normalized weapon name.</param>
/// <param name="Bullet">Position in the spray, from 1 at the press of fire.</param>
/// <param name="Fired">Sprays that reached this bullet.</param>
/// <param name="Hits">Of those bullets, the ones that hit an enemy.</param>
/// <param name="Kills">Of those bullets, the ones that killed an enemy.</param>
public sealed record SprayBulletStats(ulong SteamId, string Weapon, int Bullet, int Fired, int Hits, int Kills);

/// <summary>
/// Cuts every player's shots with full-auto guns into sprays and counts, per bullet position, how many sprays got
/// there and how many of those bullets hit and killed. Kills don't decide which shots count: every shot in a live
/// round does.
/// </summary>
/// <remarks>
/// A spray is one press of fire. The game's consecutive-shot counter (<see cref="WeaponFireEvent.ShotsFired"/>)
/// resets on release, so a spray ends when it stops climbing. A spray also ends when the gun changes, or after
/// <see cref="MaxGapSeconds"/> without a shot: the counter keeps climbing when fire is held through a reload.
/// </remarks>
public static class SprayAnalyzer
{
    /// <summary>Bump when the result changes, so stored sprays are re-analyzed.</summary>
    public const int Version = 1;

    /// <summary>Longer than any full-auto gun's time between shots.</summary>
    public const float MaxGapSeconds = 0.5f;

    /// <summary>Without the counter, a spray ends at a gap this long.</summary>
    public const float GapWithoutCounterSeconds = 0.25f;

    /// <summary>How long after a round ends its shots still count (the game waits about 7 s).</summary>
    private const int AfterRoundSeconds = 10;

    public static IReadOnlyList<SprayBulletStats> Compute(ParsedDemo demo)
    {
        var index = DemoIndex.For(demo);
        var result = new List<SprayBulletStats>();
        foreach (var steamId in demo.Players.Select(p => p.SteamId).Distinct())
        {
            result.AddRange(ComputeFor(demo, index, steamId));
        }

        return result;
    }

    private static IEnumerable<SprayBulletStats> ComputeFor(ParsedDemo demo, DemoIndex index, ulong shooter)
    {
        // A bullet that went through two players is two attributed shots on the same tick, but one bullet.
        var shotsByTick = index.ShotsBy(shooter).ToLookup(s => s.Tick);
        var tallies = new Dictionary<(string Weapon, int Bullet), int[]>();
        var maxGap = (int)Math.Round(MaxGapSeconds * demo.TickRate);
        var gapWithoutCounter = (int)Math.Round(GapWithoutCounterSeconds * demo.TickRate);

        WeaponFireEvent? previous = null;
        string? previousWeapon = null;
        var bullet = 0;
        foreach (var fire in index.FiresBy(shooter))
        {
            var weapon = StaticWeaponData.Normalize(fire.Weapon).ToLowerInvariant();
            if (!SprayWeapons.TryGetClip(weapon, out var clip) || !InLiveRound(demo, fire.Tick))
            {
                previous = null;
                continue;
            }

            var continues = previous is not null
                && previousWeapon == weapon
                && fire.Tick - previous.Tick <= maxGap
                && (fire.ShotsFired is { } now && previous.ShotsFired is { } before
                    ? now > before
                    : fire.Tick - previous.Tick <= gapWithoutCounter);
            bullet = continues ? Math.Min(bullet + 1, clip) : 1;
            previous = fire;
            previousWeapon = weapon;

            var (hit, kill) = Outcome(index, shooter, weapon, shotsByTick[fire.Tick]);
            if (!tallies.TryGetValue((weapon, bullet), out var tally))
            {
                tally = new int[3];
                tallies[(weapon, bullet)] = tally;
            }

            tally[0]++;
            tally[1] += hit ? 1 : 0;
            tally[2] += kill ? 1 : 0;
        }

        return tallies
            .OrderBy(t => t.Key.Weapon, StringComparer.Ordinal)
            .ThenBy(t => t.Key.Bullet)
            .Select(t => new SprayBulletStats(shooter, t.Key.Weapon, t.Key.Bullet, t.Value[0], t.Value[1], t.Value[2]));
    }

    /// <summary>Whether the bullet hit an opponent, and whether that hit killed one.</summary>
    private static (bool Hit, bool Kill) Outcome(DemoIndex index, ulong shooter, string weapon, IEnumerable<AttributedShot> shots)
    {
        var hit = false;
        foreach (var shot in shots)
        {
            if (shot is not { HitTick: { } hitTick, Target: { } victim })
            {
                continue;
            }

            hit = true;
            if (index.DeathOf(victim, hitTick, hitTick) is { } death
                && death.AttackerSteamId == shooter
                && StaticWeaponData.SameGun(weapon, death.Weapon))
            {
                return (true, true);
            }
        }

        return (hit, false);
    }

    private static bool InLiveRound(ParsedDemo demo, int tick)
    {
        foreach (var round in demo.Rounds)
        {
            if (tick >= round.StartTick && tick <= round.EndTick + AfterRoundSeconds * demo.TickRate)
            {
                return true;
            }
        }

        return false;
    }
}
