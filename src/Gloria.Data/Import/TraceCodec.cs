using Gloria.Core.Analysis;
using Gloria.Core.Ingestion;

namespace Gloria.Data.Import;

/// <summary>
/// Compact binary form of an <see cref="EncounterTrace"/> for the database. Angle series are half floats and
/// positions whole units in 16 bits: about 33 bytes a tick, so a typical 2 s encounter at 64 ticks is around 4 kB.
/// Format 1 had no bullet directions and format 2 no <see cref="TraceScene"/>; both still decode, with those null.
/// </summary>
public static class TraceCodec
{
    private const byte FormatVersion = 3;

    public static byte[] Encode(EncounterTrace trace)
    {
        using var buffer = new MemoryStream(64 + trace.Count * 33 + trace.Shots.Count * 13);
        using (var writer = new BinaryWriter(buffer))
        {
            writer.Write(FormatVersion);
            writer.Write(trace.StartTick);
            writer.Write((short)trace.TickRate);
            writer.Write(trace.Count);
            // A trace without bullets stores the crosshair in their place: no recoil.
            var bulletX = trace.BulletX ?? trace.ErrorX;
            var bulletY = trace.BulletY ?? trace.ErrorY;
            foreach (var series in new[] { trace.ErrorX, trace.ErrorY, trace.BodyError, trace.Speed, trace.Distance, bulletX, bulletY })
                WriteHalves(writer, series);

            var scene = trace.Scene;
            writer.Write(scene is not null);
            var names = new List<string>();
            if (scene is not null)
            {
                writer.Write((byte)scene.OpponentTeam);
                foreach (var series in new[] { scene.PlayerX, scene.PlayerY, scene.PlayerZ, scene.OpponentX, scene.OpponentY, scene.OpponentZ })
                {
                    foreach (var value in series)
                        writer.Write((short)Math.Clamp(MathF.Round(value), short.MinValue, short.MaxValue));
                }

                WriteHalves(writer, scene.PlayerYaw);
                WriteHalves(writer, scene.OpponentYaw);
                writer.Write(scene.PlayerHealth);
                writer.Write(scene.OpponentHealth);
                foreach (var duck in scene.OpponentDucking)
                    writer.Write(duck);

                names.AddRange(scene.PlayerWeapons.Select(w => w.Weapon)
                    .Concat(scene.OpponentWeapons.Select(w => w.Weapon))
                    .Concat(scene.Damage.Select(d => d.Weapon))
                    .Concat(scene.Death is { } d ? [d.Weapon] : []));
            }

            names.AddRange(trace.Shots.Select(s => s.Weapon).OfType<string>());
            names = names.Distinct().ToList();
            writer.Write((short)names.Count);
            foreach (var name in names)
                writer.Write(name);
            short Name(string? name) => name is null ? (short)-1 : (short)names.IndexOf(name);

            writer.Write((short)trace.Shots.Count);
            foreach (var shot in trace.Shots)
            {
                writer.Write(shot.Tick);
                writer.Write(shot.Hit);
                writer.Write((short)(shot.HitGroup ?? -1));
                writer.Write((Half)(shot.BulletBodyError ?? float.NaN));
                writer.Write(Name(shot.Weapon));
                writer.Write((short)(shot.Damage ?? -1));
            }

            if (scene is not null)
            {
                foreach (var weapons in new[] { scene.PlayerWeapons, scene.OpponentWeapons })
                {
                    writer.Write((short)weapons.Count);
                    foreach (var weapon in weapons)
                    {
                        writer.Write(weapon.Tick);
                        writer.Write(Name(weapon.Weapon));
                    }
                }

                writer.Write((short)scene.Damage.Count);
                foreach (var damage in scene.Damage)
                {
                    writer.Write(damage.Tick);
                    writer.Write(damage.ByPlayer);
                    writer.Write(Name(damage.Weapon));
                    writer.Write((short)damage.Damage);
                    writer.Write((short)damage.HitGroup);
                }

                writer.Write(scene.Death is not null);
                if (scene.Death is { } death)
                {
                    writer.Write(death.Tick);
                    writer.Write(death.ByPlayer);
                    writer.Write(Name(death.Weapon));
                    writer.Write(death.Headshot);
                }
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Null for an unknown format version, e.g. a trace written by a newer build.</summary>
    public static EncounterTrace? Decode(byte[]? data)
    {
        if (data is null || data.Length == 0 || data[0] is not (1 or 2 or FormatVersion))
            return null;

        using var reader = new BinaryReader(new MemoryStream(data));
        var format = reader.ReadByte();
        var hasBullets = format >= 2;
        var startTick = reader.ReadInt32();
        var tickRate = reader.ReadInt16();
        var count = reader.ReadInt32();
        float[] Halves()
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
                values[i] = (float)reader.ReadHalf();
            return values;
        }

        float[] Shorts()
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
                values[i] = reader.ReadInt16();
            return values;
        }

        var x = Halves();
        var y = Halves();
        var body = Halves();
        var speed = Halves();
        var distance = Halves();
        var bulletX = hasBullets ? Halves() : null;
        var bulletY = hasBullets ? Halves() : null;

        var hasScene = format >= 3 && reader.ReadBoolean();
        var team = Team.None;
        float[] playerX = [], playerY = [], playerZ = [], opponentX = [], opponentY = [], opponentZ = [], playerYaw = [], opponentYaw = [];
        byte[] playerHealth = [], opponentHealth = [];
        var ducking = Array.Empty<bool>();
        if (hasScene)
        {
            team = (Team)reader.ReadByte();
            (playerX, playerY, playerZ) = (Shorts(), Shorts(), Shorts());
            (opponentX, opponentY, opponentZ) = (Shorts(), Shorts(), Shorts());
            (playerYaw, opponentYaw) = (Halves(), Halves());
            playerHealth = reader.ReadBytes(count);
            opponentHealth = reader.ReadBytes(count);
            ducking = new bool[count];
            for (var i = 0; i < count; i++)
                ducking[i] = reader.ReadBoolean();
        }

        var names = new string[format >= 3 ? reader.ReadInt16() : 0];
        for (var i = 0; i < names.Length; i++)
            names[i] = reader.ReadString();
        string? Name(short index) => index >= 0 && index < names.Length ? names[index] : null;

        var shots = new TraceShot[reader.ReadInt16()];
        for (var i = 0; i < shots.Length; i++)
        {
            var tick = reader.ReadInt32();
            var hit = reader.ReadBoolean();
            var group = reader.ReadInt16();
            var bullet = hasBullets ? (float)reader.ReadHalf() : float.NaN;
            string? weapon = null;
            short damage = -1;
            if (format >= 3)
            {
                weapon = Name(reader.ReadInt16());
                damage = reader.ReadInt16();
            }

            shots[i] = new TraceShot(tick, hit, group < 0 ? null : group, float.IsNaN(bullet) ? null : bullet, weapon, damage < 0 ? null : damage);
        }

        TraceScene? scene = null;
        if (hasScene)
        {
            List<TraceWeapon> Weapons()
            {
                var weapons = new List<TraceWeapon>(reader.ReadInt16());
                for (var i = 0; i < weapons.Capacity; i++)
                    weapons.Add(new TraceWeapon(reader.ReadInt32(), Name(reader.ReadInt16()) ?? ""));
                return weapons;
            }

            var playerWeapons = Weapons();
            var opponentWeapons = Weapons();
            var damage = new TraceDamage[reader.ReadInt16()];
            for (var i = 0; i < damage.Length; i++)
                damage[i] = new TraceDamage(reader.ReadInt32(), reader.ReadBoolean(), Name(reader.ReadInt16()) ?? "", reader.ReadInt16(), reader.ReadInt16());
            TraceDeath? death = reader.ReadBoolean()
                ? new TraceDeath(reader.ReadInt32(), reader.ReadBoolean(), Name(reader.ReadInt16()) ?? "", reader.ReadBoolean())
                : null;
            scene = new TraceScene(
                team,
                playerX, playerY, playerZ, playerYaw, playerHealth,
                opponentX, opponentY, opponentZ, opponentYaw, opponentHealth, ducking,
                playerWeapons, opponentWeapons, damage, death);
        }

        return new EncounterTrace(startTick, tickRate, x, y, body, speed, distance, shots, bulletX, bulletY, scene);
    }

    private static void WriteHalves(BinaryWriter writer, float[] series)
    {
        foreach (var value in series)
            writer.Write((Half)value);
    }
}
