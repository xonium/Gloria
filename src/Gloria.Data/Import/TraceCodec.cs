using Gloria.Core.Analysis;

namespace Gloria.Data.Import;

/// <summary>
/// Compact binary form of an <see cref="EncounterTrace"/> for the database. Samples are stored as
/// half floats: about 10 bytes a tick, so a typical 2 s encounter at 64 ticks is around 1.6 kB.
/// </summary>
public static class TraceCodec
{
    private const byte FormatVersion = 1;

    public static byte[] Encode(EncounterTrace trace)
    {
        using var buffer = new MemoryStream(16 + trace.Count * 10 + trace.Shots.Count * 7);
        using (var writer = new BinaryWriter(buffer))
        {
            writer.Write(FormatVersion);
            writer.Write(trace.StartTick);
            writer.Write((short)trace.TickRate);
            writer.Write(trace.Count);
            foreach (var series in new[] { trace.ErrorX, trace.ErrorY, trace.BodyError, trace.Speed, trace.Distance })
            {
                foreach (var value in series)
                    writer.Write((Half)value);
            }

            writer.Write((short)trace.Shots.Count);
            foreach (var shot in trace.Shots)
            {
                writer.Write(shot.Tick);
                writer.Write(shot.Hit);
                writer.Write((short)(shot.HitGroup ?? -1));
            }
        }

        return buffer.ToArray();
    }

    /// <summary>Null for an unknown format version, e.g. a trace written by a newer build.</summary>
    public static EncounterTrace? Decode(byte[]? data)
    {
        if (data is null || data.Length == 0 || data[0] != FormatVersion)
            return null;

        using var reader = new BinaryReader(new MemoryStream(data));
        reader.ReadByte();
        var startTick = reader.ReadInt32();
        var tickRate = reader.ReadInt16();
        var count = reader.ReadInt32();
        float[] Series()
        {
            var values = new float[count];
            for (var i = 0; i < count; i++)
                values[i] = (float)reader.ReadHalf();
            return values;
        }

        var x = Series();
        var y = Series();
        var body = Series();
        var speed = Series();
        var distance = Series();
        var shots = new TraceShot[reader.ReadInt16()];
        for (var i = 0; i < shots.Length; i++)
        {
            var tick = reader.ReadInt32();
            var hit = reader.ReadBoolean();
            var group = reader.ReadInt16();
            shots[i] = new TraceShot(tick, hit, group < 0 ? null : group);
        }

        return new EncounterTrace(startTick, tickRate, x, y, body, speed, distance, shots);
    }
}
