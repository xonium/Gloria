using System.Numerics;

namespace Gloria.Core.Ingestion;

/// <summary>
/// Per-tick state of every tracked player, stored column-wise so a 40-minute demo stays
/// around 85 MB. Lookups by tick are O(1): a tick that was not recorded resolves to the
/// latest recorded tick before it.
/// </summary>
public sealed class PlayerTimeline
{
    [Flags]
    internal enum StateFlags : byte
    {
        None = 0,
        Present = 1,
        Alive = 2,
        Scoped = 4,
        OnGround = 8,
        Ducking = 16,
    }

    private readonly Dictionary<ulong, int> _indexBySteamId;
    private readonly int _firstTick;
    private readonly int[] _rowOfTick;

    internal PlayerTimeline(
        IReadOnlyList<ulong> steamIds,
        IReadOnlyList<string> weaponNames,
        int[] recordedTicks,
        PlayerColumns[] columns)
    {
        SteamIds = steamIds;
        WeaponNames = weaponNames;
        RecordedTicks = recordedTicks;
        Columns = columns;
        _indexBySteamId = steamIds.Select((id, i) => (id, i)).ToDictionary(p => p.id, p => p.i);

        _firstTick = recordedTicks.Length > 0 ? recordedTicks[0] : 0;
        var lastTick = recordedTicks.Length > 0 ? recordedTicks[^1] : -1;
        _rowOfTick = new int[Math.Max(0, lastTick - _firstTick + 1)];
        var row = 0;
        for (var t = 0; t < _rowOfTick.Length; t++)
        {
            while (row + 1 < recordedTicks.Length && recordedTicks[row + 1] <= t + _firstTick)
                row++;
            _rowOfTick[t] = row;
        }
    }

    public static PlayerTimeline Empty { get; } = new([], [], [], []);

    /// <summary>Tracked players; a player's position in this list is its local index.</summary>
    public IReadOnlyList<ulong> SteamIds { get; }

    public IReadOnlyList<string> WeaponNames { get; }

    public IReadOnlyList<int> RecordedTicks { get; }

    internal PlayerColumns[] Columns { get; }

    public int FirstTick => RecordedTicks.Count > 0 ? RecordedTicks[0] : 0;

    public int LastTick => RecordedTicks.Count > 0 ? RecordedTicks[^1] : -1;

    /// <summary>True when any player was ever spotted, i.e. the demo carries spotted state.</summary>
    public bool HasSpottedData => _hasSpottedData ??= Columns.Any(c => c.SpottedBy.Any(m => m != 0));

    private bool? _hasSpottedData;

    public int IndexOf(ulong steamId) => _indexBySteamId.GetValueOrDefault(steamId, -1);

    /// <summary>Row holding the state at <paramref name="tick"/>, or -1 outside the recorded range.</summary>
    public int RowOf(int tick)
    {
        var i = tick - _firstTick;
        return i >= 0 && i < _rowOfTick.Length ? _rowOfTick[i] : -1;
    }

    public bool TryGet(ulong steamId, int tick, out PlayerTickState state)
    {
        var player = IndexOf(steamId);
        var row = RowOf(tick);
        if (player < 0 || row < 0 || !Columns[player].Flags[row].HasFlag(StateFlags.Present))
        {
            state = default;
            return false;
        }

        state = Read(player, row);
        return true;
    }

    /// <summary>Whether <paramref name="observerSteamId"/> had <paramref name="targetSteamId"/> spotted at the tick.</summary>
    public bool IsSpottedBy(ulong targetSteamId, ulong observerSteamId, int tick)
    {
        var target = IndexOf(targetSteamId);
        var observer = IndexOf(observerSteamId);
        var row = RowOf(tick);
        if (target < 0 || observer is < 0 or >= 64 || row < 0)
            return false;

        return (Columns[target].SpottedBy[row] & (1UL << observer)) != 0;
    }

    private PlayerTickState Read(int player, int row)
    {
        var c = Columns[player];
        var flags = c.Flags[row];
        return new PlayerTickState(
            RecordedTicks[row],
            SteamIds[player],
            new Vector3(c.X[row], c.Y[row], c.Z[row]),
            new Vector3(c.VX[row], c.VY[row], c.VZ[row]),
            c.Yaw[row],
            c.Pitch[row],
            WeaponNames[c.Weapon[row]],
            flags.HasFlag(StateFlags.Alive),
            (Team)c.Team[row],
            c.EyeHeight[row],
            flags.HasFlag(StateFlags.Scoped),
            flags.HasFlag(StateFlags.OnGround),
            flags.HasFlag(StateFlags.Ducking),
            c.Health[row],
            c.PunchYaw[row],
            c.PunchPitch[row]);
    }

    internal sealed class PlayerColumns(int capacity)
    {
        public float[] X = new float[capacity];
        public float[] Y = new float[capacity];
        public float[] Z = new float[capacity];
        public float[] VX = new float[capacity];
        public float[] VY = new float[capacity];
        public float[] VZ = new float[capacity];
        public float[] Yaw = new float[capacity];
        public float[] Pitch = new float[capacity];
        public float[] EyeHeight = new float[capacity];
        public float[] PunchYaw = new float[capacity];
        public float[] PunchPitch = new float[capacity];
        public ushort[] Weapon = new ushort[capacity];
        public StateFlags[] Flags = new StateFlags[capacity];
        public byte[] Team = new byte[capacity];
        public byte[] Health = new byte[capacity];
        public ulong[] SpottedBy = new ulong[capacity];

        public void Resize(int size)
        {
            Array.Resize(ref X, size);
            Array.Resize(ref Y, size);
            Array.Resize(ref Z, size);
            Array.Resize(ref VX, size);
            Array.Resize(ref VY, size);
            Array.Resize(ref VZ, size);
            Array.Resize(ref Yaw, size);
            Array.Resize(ref Pitch, size);
            Array.Resize(ref EyeHeight, size);
            Array.Resize(ref PunchYaw, size);
            Array.Resize(ref PunchPitch, size);
            Array.Resize(ref Weapon, size);
            Array.Resize(ref Flags, size);
            Array.Resize(ref Team, size);
            Array.Resize(ref Health, size);
            Array.Resize(ref SpottedBy, size);
        }
    }
}

/// <summary>Builds a <see cref="PlayerTimeline"/> one tick at a time. Used by the parser and by test fixtures.</summary>
public sealed class PlayerTimelineBuilder
{
    private readonly List<ulong> _steamIds = [];
    private readonly Dictionary<ulong, int> _indexBySteamId = [];
    private readonly List<string> _weaponNames = [""];
    private readonly Dictionary<string, ushort> _weaponIds = new(StringComparer.Ordinal) { [""] = 0 };
    private readonly List<PlayerTimeline.PlayerColumns> _columns = [];
    private int[] _ticks = new int[1024];
    private int _rows;

    public int RowCount => _rows;

    public int CurrentTick => _rows > 0 ? _ticks[_rows - 1] : int.MinValue;

    /// <summary>Starts a new row. Ticks must increase; calling again with the current tick keeps the row.</summary>
    public void BeginTick(int tick)
    {
        if (_rows > 0 && tick == _ticks[_rows - 1])
            return;
        if (_rows > 0 && tick < _ticks[_rows - 1])
            throw new ArgumentOutOfRangeException(nameof(tick), tick, "Ticks must be recorded in increasing order.");

        if (_rows == _ticks.Length)
        {
            var size = _ticks.Length * 2;
            Array.Resize(ref _ticks, size);
            foreach (var c in _columns)
                c.Resize(size);
        }

        _ticks[_rows++] = tick;
    }

    /// <summary>Local index of the player, registering them on first sight.</summary>
    public int IndexOf(ulong steamId)
    {
        if (_indexBySteamId.TryGetValue(steamId, out var index))
            return index;

        index = _steamIds.Count;
        _steamIds.Add(steamId);
        _indexBySteamId[steamId] = index;
        _columns.Add(new PlayerTimeline.PlayerColumns(_ticks.Length));
        return index;
    }

    /// <summary>Writes the player's state into the current row.</summary>
    /// <param name="spottedBy">Bit i set when the player with local index i has this player spotted.</param>
    public void Set(int playerIndex, in PlayerTickState state, ulong spottedBy = 0)
    {
        if (_rows == 0)
            throw new InvalidOperationException("Call BeginTick first.");

        var row = _rows - 1;
        var c = _columns[playerIndex];
        c.X[row] = state.Position.X;
        c.Y[row] = state.Position.Y;
        c.Z[row] = state.Position.Z;
        c.VX[row] = state.Velocity.X;
        c.VY[row] = state.Velocity.Y;
        c.VZ[row] = state.Velocity.Z;
        c.Yaw[row] = state.Yaw;
        c.Pitch[row] = state.Pitch;
        c.EyeHeight[row] = state.EyeHeight;
        c.PunchYaw[row] = state.AimPunchYaw;
        c.PunchPitch[row] = state.AimPunchPitch;
        c.Weapon[row] = WeaponId(state.ActiveWeapon);
        c.Team[row] = (byte)state.Team;
        c.Health[row] = (byte)Math.Clamp(state.Health, 0, 255);
        c.SpottedBy[row] = spottedBy;

        var flags = PlayerTimeline.StateFlags.Present;
        if (state.IsAlive) flags |= PlayerTimeline.StateFlags.Alive;
        if (state.IsScoped) flags |= PlayerTimeline.StateFlags.Scoped;
        if (state.IsOnGround) flags |= PlayerTimeline.StateFlags.OnGround;
        if (state.IsDucking) flags |= PlayerTimeline.StateFlags.Ducking;
        c.Flags[row] = flags;
    }

    public void Set(in PlayerTickState state, ulong spottedBy = 0) => Set(IndexOf(state.SteamId), state, spottedBy);

    /// <summary>Whether any present sample has a non-zero horizontal velocity.</summary>
    public bool HasNetworkedVelocity()
    {
        foreach (var c in _columns)
        {
            for (var row = 0; row < _rows; row++)
            {
                if (c.Flags[row].HasFlag(PlayerTimeline.StateFlags.Present) && (c.VX[row] != 0 || c.VY[row] != 0))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Replaces velocity with the position change since the previous row, for demos that do not network it.</summary>
    public void DeriveVelocityFromPositions(int tickRate)
    {
        foreach (var c in _columns)
        {
            for (var row = _rows - 1; row >= 0; row--)
            {
                var prev = row - 1;
                if (prev < 0
                    || !c.Flags[row].HasFlag(PlayerTimeline.StateFlags.Present)
                    || !c.Flags[prev].HasFlag(PlayerTimeline.StateFlags.Present))
                {
                    c.VX[row] = c.VY[row] = c.VZ[row] = 0;
                    continue;
                }

                var dt = (_ticks[row] - _ticks[prev]) / (float)tickRate;
                c.VX[row] = (c.X[row] - c.X[prev]) / dt;
                c.VY[row] = (c.Y[row] - c.Y[prev]) / dt;
                c.VZ[row] = (c.Z[row] - c.Z[prev]) / dt;
            }
        }
    }

    public PlayerTimeline Build()
    {
        foreach (var c in _columns)
            c.Resize(_rows);

        return new PlayerTimeline([.. _steamIds], [.. _weaponNames], _ticks[.._rows], [.. _columns]);
    }

    private ushort WeaponId(string weapon)
    {
        if (_weaponIds.TryGetValue(weapon, out var id))
            return id;

        id = checked((ushort)_weaponNames.Count);
        _weaponNames.Add(weapon);
        _weaponIds[weapon] = id;
        return id;
    }
}
