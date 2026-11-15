using Gloria.Core.Encounters;
using Gloria.Core.Ingestion;
using Gloria.Core.Metrics;
using Gloria.Core.Weapons;

namespace Gloria.Core.Analysis;

/// <summary>
/// A shot inside a trace. Its crosshair and bullet positions are the samples one tick before <see cref="Tick"/>,
/// which match where the shot went best.
/// </summary>
/// <param name="HitGroup">The game's hit group of the damage it dealt, or null for a miss.</param>
/// <param name="BulletBodyError">
/// Angle from the bullet direction (crosshair plus recoil) to the nearest point of the body, 0 when on it.
/// Null for traces stored before recoil was parsed.
/// </param>
/// <param name="Weapon">Normalized weapon name, or null for traces stored before format 3.</param>
/// <param name="Damage">Health the hit took (all pellets of a shotgun shot), or null for a miss or an older trace.</param>
public readonly record struct TraceShot(
    int Tick,
    bool Hit,
    int? HitGroup,
    float? BulletBodyError = null,
    string? Weapon = null,
    int? Damage = null);

/// <summary>The weapon a player held from <paramref name="Tick"/> on (normalized name, empty for none).</summary>
public readonly record struct TraceWeapon(int Tick, string Weapon);

/// <summary>Damage between the two players: shots, utility, the knife.</summary>
/// <param name="ByPlayer">True when the player hurt the opponent, false when the opponent hurt the player.</param>
public readonly record struct TraceDamage(int Tick, bool ByPlayer, string Weapon, int Damage, int HitGroup);

/// <summary>The death that ended the encounter, when one of the two killed the other.</summary>
public readonly record struct TraceDeath(int Tick, bool ByPlayer, string Weapon, bool Headshot);

/// <summary>
/// What happened in the world during an encounter, for the map, the agent model and the stats: positions, yaw,
/// health, weapons, damage and the deciding death. Series are per tick, aligned with the trace samples.
/// </summary>
/// <param name="OpponentTeam">Picks the agent model.</param>
/// <param name="PlayerYaw">The player's view yaw in degrees, as the game stores it (counter-clockwise from +X).</param>
public sealed record TraceScene(
    Team OpponentTeam,
    float[] PlayerX,
    float[] PlayerY,
    float[] PlayerZ,
    float[] PlayerYaw,
    byte[] PlayerHealth,
    float[] OpponentX,
    float[] OpponentY,
    float[] OpponentZ,
    float[] OpponentYaw,
    byte[] OpponentHealth,
    bool[] OpponentDucking,
    IReadOnlyList<TraceWeapon> PlayerWeapons,
    IReadOnlyList<TraceWeapon> OpponentWeapons,
    IReadOnlyList<TraceDamage> Damage,
    TraceDeath? Death)
{
    /// <summary>The weapon held at <paramref name="tick"/>, or null before the first entry or when unarmed.</summary>
    public static string? WeaponAt(IReadOnlyList<TraceWeapon> weapons, int tick)
    {
        string? weapon = null;
        foreach (var change in weapons)
        {
            if (change.Tick > tick)
                break;
            weapon = change.Weapon;
        }

        return string.IsNullOrEmpty(weapon) ? null : weapon;
    }
}

/// <summary>
/// Per-tick samples of one encounter, for charts and the replay. Starts <see cref="EncounterTraceBuilder.LeadSeconds"/>
/// before the encounter (the spotted flag lags) and ends with it.
/// </summary>
/// <param name="ErrorX">Crosshair offset from the opponent's upper chest in degrees, positive to the right on screen.</param>
/// <param name="ErrorY">Crosshair offset from the upper chest in degrees, positive upwards.</param>
/// <param name="BodyError">
/// Angle to the nearest point of the body (0 when on it), negative once the crosshair has passed the body
/// relative to where it started.
/// </param>
/// <param name="Speed">The player's horizontal speed in units per second.</param>
/// <param name="Distance">Eye-to-chest distance in units, for drawing the body to scale.</param>
/// <param name="BulletX">
/// Bullet direction (crosshair plus recoil, see <see cref="Recoil"/>) in the same space as <paramref name="ErrorX"/>.
/// Null for traces stored before recoil was parsed.
/// </param>
/// <param name="BulletY">Bullet direction in the same space as <paramref name="ErrorY"/>, or null.</param>
/// <param name="Scene">Positions, health, weapons and damage; null for traces stored before format 3.</param>
public sealed record EncounterTrace(
    int StartTick,
    int TickRate,
    float[] ErrorX,
    float[] ErrorY,
    float[] BodyError,
    float[] Speed,
    float[] Distance,
    IReadOnlyList<TraceShot> Shots,
    float[]? BulletX = null,
    float[]? BulletY = null,
    TraceScene? Scene = null)
{
    public int Count => ErrorX.Length;

    public bool HasBullets => BulletX is not null && BulletY is not null;

    /// <summary>Seconds of sample <paramref name="index"/> relative to <paramref name="encounterStartTick"/>.</summary>
    public float SecondsAt(int index, int encounterStartTick) => (StartTick + index - encounterStartTick) / (float)TickRate;

    /// <summary>Sample index of a tick, clamped to the trace.</summary>
    public int IndexOf(int tick) => Math.Clamp(tick - StartTick, 0, Math.Max(Count - 1, 0));
}

public static class EncounterTraceBuilder
{
    public const float LeadSeconds = 0.5f;

    /// <summary>Null when the player or opponent has no recorded state in the encounter.</summary>
    public static EncounterTrace? Build(ParsedDemo demo, Encounter encounter)
    {
        var timeline = demo.Timeline;
        var start = Math.Max(timeline.FirstTick, encounter.StartTick - (int)(LeadSeconds * demo.TickRate));
        var count = encounter.EndTick - start + 1;
        if (count <= 0)
            return null;

        var x = new float[count];
        var y = new float[count];
        var body = new float[count];
        var speed = new float[count];
        var distance = new float[count];
        var bulletX = new float[count];
        var bulletY = new float[count];
        var playerX = new float[count];
        var playerY = new float[count];
        var playerZ = new float[count];
        var playerYaw = new float[count];
        var playerHealth = new byte[count];
        var opponentX = new float[count];
        var opponentY = new float[count];
        var opponentZ = new float[count];
        var opponentYaw = new float[count];
        var opponentHealth = new byte[count];
        var ducking = new bool[count];
        var playerWeapons = new List<TraceWeapon>();
        var opponentWeapons = new List<TraceWeapon>();
        var opponentTeam = Team.None;
        var reference = System.Numerics.Vector2.Zero;
        var any = false;

        for (var i = 0; i < count; i++)
        {
            var tick = start + i;
            if (!timeline.TryGet(encounter.PlayerSteamId, tick, out var me)
                || !timeline.TryGet(encounter.OpponentSteamId, tick, out var them))
            {
                // Gaps repeat the previous sample so the replay doesn't jump to the origin.
                if (i > 0)
                {
                    x[i] = x[i - 1];
                    y[i] = y[i - 1];
                    body[i] = body[i - 1];
                    speed[i] = speed[i - 1];
                    distance[i] = distance[i - 1];
                    bulletX[i] = bulletX[i - 1];
                    bulletY[i] = bulletY[i - 1];
                    playerX[i] = playerX[i - 1];
                    playerY[i] = playerY[i - 1];
                    playerZ[i] = playerZ[i - 1];
                    playerYaw[i] = playerYaw[i - 1];
                    playerHealth[i] = playerHealth[i - 1];
                    opponentX[i] = opponentX[i - 1];
                    opponentY[i] = opponentY[i - 1];
                    opponentZ[i] = opponentZ[i - 1];
                    opponentYaw[i] = opponentYaw[i - 1];
                    opponentHealth[i] = opponentHealth[i - 1];
                    ducking[i] = ducking[i - 1];
                }

                continue;
            }

            any = true;
            var signed = BodyModel.SignedError(me, them);
            // SignedError is view minus target: more yaw is further left, more pitch is further down.
            x[i] = -signed.X;
            y[i] = -signed.Y;
            if (tick <= encounter.StartTick || reference == System.Numerics.Vector2.Zero)
                reference = new System.Numerics.Vector2(x[i], y[i]);

            var error = BodyModel.AngularError(me, them);
            var passed = System.Numerics.Vector2.Dot(reference, new System.Numerics.Vector2(x[i], y[i])) < 0;
            body[i] = passed ? -error : error;
            speed[i] = me.HorizontalSpeed;
            distance[i] = BodyModel.Distance(me, them);
            var bullet = BodyModel.SignedError(Recoil.BulletView(me), them);
            bulletX[i] = -bullet.X;
            bulletY[i] = -bullet.Y;

            (playerX[i], playerY[i], playerZ[i]) = (me.Position.X, me.Position.Y, me.Position.Z);
            playerYaw[i] = me.Yaw;
            playerHealth[i] = (byte)Math.Clamp(me.Health, 0, 255);
            (opponentX[i], opponentY[i], opponentZ[i]) = (them.Position.X, them.Position.Y, them.Position.Z);
            opponentYaw[i] = them.Yaw;
            opponentHealth[i] = (byte)Math.Clamp(them.Health, 0, 255);
            ducking[i] = them.IsDucking;
            opponentTeam = them.Team;
            Track(playerWeapons, tick, me.ActiveWeapon);
            Track(opponentWeapons, tick, them.ActiveWeapon);
        }

        if (!any)
            return null;

        var index = DemoIndex.For(demo);
        var shots = index.ShotsBy(encounter.PlayerSteamId)
            .Where(s => s.Target == encounter.OpponentSteamId && s.Tick >= encounter.StartTick && s.Tick <= encounter.EndTick)
            .Select(s =>
            {
                var hurts = s.HitTick is { } hitTick ? HurtsOf(index, encounter, hitTick, s.Weapon) : [];
                return new TraceShot(
                    s.Tick,
                    s.HitTick is not null,
                    hurts.Count > 0 ? hurts[0].HitGroup : null,
                    BulletBodyError(timeline, encounter, s.Tick - 1),
                    s.Weapon,
                    hurts.Count > 0 ? hurts.Sum(h => h.DamageHealth) : null);
            })
            .ToList();

        var end = encounter.EndTick;
        var damage = index.HurtsBy(encounter.PlayerSteamId)
            .Where(h => h.VictimSteamId == encounter.OpponentSteamId)
            .Select(h => (Hurt: h, ByPlayer: true))
            .Concat(index.HurtsBy(encounter.OpponentSteamId)
                .Where(h => h.VictimSteamId == encounter.PlayerSteamId)
                .Select(h => (Hurt: h, ByPlayer: false)))
            .Where(d => d.Hurt.Tick >= start && d.Hurt.Tick <= end)
            .OrderBy(d => d.Hurt.Tick)
            .Select(d => new TraceDamage(
                d.Hurt.Tick,
                d.ByPlayer,
                Variant(timeline, d.ByPlayer ? encounter.PlayerSteamId : encounter.OpponentSteamId, d.Hurt),
                d.Hurt.DamageHealth,
                d.Hurt.HitGroup))
            .ToList();
        TraceDeath? death =
            index.DeathOf(encounter.OpponentSteamId, start, end) is { } kill && kill.AttackerSteamId == encounter.PlayerSteamId
                ? new TraceDeath(kill.Tick, true, StaticWeaponData.Normalize(kill.Weapon), kill.Headshot)
            : index.DeathOf(encounter.PlayerSteamId, start, end) is { } died && died.AttackerSteamId == encounter.OpponentSteamId
                ? new TraceDeath(died.Tick, false, StaticWeaponData.Normalize(died.Weapon), died.Headshot)
            : null;

        var scene = new TraceScene(
            opponentTeam,
            playerX, playerY, playerZ, playerYaw, playerHealth,
            opponentX, opponentY, opponentZ, opponentYaw, opponentHealth, ducking,
            playerWeapons, opponentWeapons, damage, death);
        return new EncounterTrace(start, demo.TickRate, x, y, body, speed, distance, shots, bulletX, bulletY, scene);
    }

    /// <summary>
    /// Hurt events name the base item (m4a1 for an M4A1-S); the attacker's held weapon names the variant.
    /// </summary>
    private static string Variant(PlayerTimeline timeline, ulong attacker, PlayerHurtEvent hurt)
    {
        var weapon = StaticWeaponData.Normalize(hurt.Weapon);
        return timeline.TryGet(attacker, hurt.Tick, out var state) && StaticWeaponData.Normalize(state.ActiveWeapon ?? "") is { Length: > 0 } held
            && StaticWeaponData.SameGun(held, weapon)
                ? held
                : weapon;
    }

    private static void Track(List<TraceWeapon> weapons, int tick, string? weapon)
    {
        var name = StaticWeaponData.Normalize(weapon ?? "");
        if (weapons.Count == 0 || weapons[^1].Weapon != name)
            weapons.Add(new TraceWeapon(tick, name));
    }

    private static float? BulletBodyError(PlayerTimeline timeline, Encounter encounter, int tick) =>
        timeline.TryGet(encounter.PlayerSteamId, tick, out var me) && timeline.TryGet(encounter.OpponentSteamId, tick, out var them)
            ? BodyModel.AngularError(Recoil.BulletView(me), them)
            : null;

    /// <summary>The hurts a hit dealt: one for a bullet, several for shotgun pellets.</summary>
    private static List<PlayerHurtEvent> HurtsOf(DemoIndex index, Encounter encounter, int hitTick, string weapon) =>
        index.HurtsBy(encounter.PlayerSteamId)
            .Where(h => h.Tick == hitTick && h.VictimSteamId == encounter.OpponentSteamId && StaticWeaponData.SameGun(weapon, h.Weapon))
            .ToList();
}
