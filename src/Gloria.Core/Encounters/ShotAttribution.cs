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
/// <see cref="HitWindowTicks"/> belongs to that opponent (once per victim when it hit several); otherwise it
/// belongs to the alive opponent nearest the crosshair, if within <see cref="MaxAttributionDegrees"/>.
/// Hurt events name a gun's base item (<see cref="StaticWeaponData.HurtName"/>), not the variant fired.
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

            var hits = hurts.Where(h =>
                    h.Tick >= fire.Tick && h.Tick <= fire.Tick + HitWindowTicks
                    && !usedHurts.Contains(h)
                    && StaticWeaponData.SameGun(weapon, h.Weapon)
                    && IsOpponent(demo, me, h.VictimSteamId, fire.Tick))
                .ToList();
            if (hits.Count > 0)
            {
                // One shot per victim: a bullet that goes through one opponent into another hit both.
                // Shotgun pellets that hit the same victim belong to the same shot.
                foreach (var victim in hits.GroupBy(h => h.VictimSteamId))
                {
                    var first = victim.First();
                    usedHurts.UnionWith(victim.Where(h => h.Tick == first.Tick));
                    shots.Add(new AttributedShot(fire.Tick, weapon, victim.Key, first.Tick));
                }

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
