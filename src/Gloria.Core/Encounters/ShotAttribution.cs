using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;

namespace Gloria.Core.Encounters;

/// <summary>A gun shot and the opponent it was aimed at, if one could be determined.</summary>
/// <param name="Weapon">Normalized weapon name, without the <c>weapon_</c> prefix.</param>
/// <param name="HitTick">Tick of the damage this shot dealt to <paramref name="Target"/>, if it hit.</param>
public readonly record struct AttributedShot(int Tick, string Weapon, ulong? Target, int? HitTick);

/// <summary>
/// Decides which opponent each shot was aimed at. A shot that damaged an opponent within
/// <see cref="HitWindowTicks"/> belongs to that opponent; otherwise it belongs to the alive opponent
/// nearest the crosshair, if within <see cref="MaxAttributionDegrees"/>.
/// </summary>
public static class ShotAttribution
{
    /// <summary>Damage is reported on the fire tick or shortly after it.</summary>
    public const int HitWindowTicks = 2;

    public const float MaxAttributionDegrees = 15f;

    internal static IReadOnlyList<AttributedShot> Attribute(ParsedDemo demo, DemoIndex index, ulong shooter, IWeaponData? weapons = null)
    {
        weapons ??= StaticWeaponData.Instance;
        var hurts = index.HurtsBy(shooter).ToList();
        var shots = new List<AttributedShot>();
        var usedHurts = new HashSet<PlayerHurtEvent>(ReferenceEqualityComparer.Instance);

        foreach (var fire in index.FiresBy(shooter))
        {
            var weapon = StaticWeaponData.Normalize(fire.Weapon);
            if (weapons.Classify(weapon) == WeaponClass.Other
                || !demo.Timeline.TryGet(shooter, fire.Tick, out var me))
            {
                continue;
            }

            var hit = hurts.FirstOrDefault(h =>
                h.Tick >= fire.Tick && h.Tick <= fire.Tick + HitWindowTicks
                && !usedHurts.Contains(h)
                && string.Equals(StaticWeaponData.Normalize(h.Weapon), weapon, StringComparison.OrdinalIgnoreCase)
                && IsOpponent(demo, me, h.VictimSteamId, fire.Tick));
            if (hit is not null)
            {
                // Shotguns fire one event that deals several hurts; only the first claims it.
                usedHurts.Add(hit);
                shots.Add(new AttributedShot(fire.Tick, weapon, hit.VictimSteamId, hit.Tick));
                continue;
            }

            shots.Add(new AttributedShot(fire.Tick, weapon, NearestOpponent(demo, me, fire.Tick), null));
        }

        return shots;
    }

    private static ulong? NearestOpponent(ParsedDemo demo, in PlayerTickState me, int tick)
    {
        ulong? best = null;
        var bestError = MaxAttributionDegrees;
        foreach (var id in demo.Timeline.SteamIds)
        {
            if (!demo.Timeline.TryGet(id, tick, out var other) || !other.IsAlive || !AreOpponents(me.Team, other.Team))
            {
                continue;
            }

            var error = BodyModel.AngularError(me, other);
            if (error <= bestError)
            {
                bestError = error;
                best = id;
            }
        }

        return best;
    }

    private static bool IsOpponent(ParsedDemo demo, in PlayerTickState me, ulong other, int tick) =>
        demo.Timeline.TryGet(other, tick, out var state) && AreOpponents(me.Team, state.Team);

    public static bool AreOpponents(Team a, Team b) =>
        (a, b) is (Team.Terrorist, Team.CounterTerrorist) or (Team.CounterTerrorist, Team.Terrorist);
}
