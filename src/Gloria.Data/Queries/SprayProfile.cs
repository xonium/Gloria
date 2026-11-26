namespace Gloria.Data.Queries;

/// <summary>One player's sprays with every full-auto gun they used, over a set of demos.</summary>
/// <param name="Guns">Most sprays first.</param>
/// <param name="Demos">Demos counted (analyzed by the current version).</param>
/// <param name="OlderVersionDemos">The player's demos left out because another analysis version produced them.</param>
public sealed record SprayProfile(IReadOnlyList<GunSpray> Guns, int Demos, int OlderVersionDemos)
{
    public GunSpray? Gun(string weapon) => Guns.FirstOrDefault(g => g.Weapon == weapon);
}

/// <summary>A player's bullets at one position of their sprays with one gun.</summary>
/// <param name="Bullet">Position in the spray, from 1 at the press of fire.</param>
/// <param name="Fired">Sprays that reached this bullet.</param>
public sealed record SprayBullet(int Bullet, int Fired, int Hits, int Kills);

/// <summary>
/// A player's sprays with one gun: how far they got, and how often each bullet hit and killed.
/// </summary>
/// <param name="Weapon">Normalized weapon name, e.g. <c>ak47</c>.</param>
/// <param name="Clip">Magazine size; the bullets run from 1 to this.</param>
/// <param name="Bullets">One per position, 1 to <paramref name="Clip"/>, zeros included.</param>
public sealed record GunSpray(string Weapon, int Clip, IReadOnlyList<SprayBullet> Bullets)
{
    /// <summary>Below this many bullets at a position, its hit rate is too noisy to show.</summary>
    public const int MinBulletsForHitRate = 10;

    /// <summary>Every spray has a first bullet.</summary>
    public int Sprays => Bullets.Count > 0 ? Bullets[0].Fired : 0;

    public int Shots => Bullets.Sum(b => b.Fired);

    public int Hits => Bullets.Sum(b => b.Hits);

    public int Kills => Bullets.Sum(b => b.Kills);

    /// <summary>Share of all bullets that hit; NaN without shots.</summary>
    public double HitRate => Shots == 0 ? double.NaN : Hits / (double)Shots;

    /// <summary>Share of sprays that reached bullet <paramref name="bullet"/> (1-based); NaN without sprays.</summary>
    public double Reach(int bullet) => Sprays == 0 ? double.NaN : At(bullet).Fired / (double)Sprays;

    /// <summary>
    /// Share of bullet <paramref name="bullet"/>'s shots that hit; NaN with fewer than
    /// <see cref="MinBulletsForHitRate"/> of them.
    /// </summary>
    public double HitRateAt(int bullet)
    {
        var b = At(bullet);
        return b.Fired < MinBulletsForHitRate ? double.NaN : b.Hits / (double)b.Fired;
    }

    /// <summary>The bullet that killed most often, the earlier one on a tie; null without kills.</summary>
    public int? KillBullet => Kills == 0
        ? null
        : Bullets.OrderByDescending(b => b.Kills).ThenBy(b => b.Bullet).First().Bullet;

    /// <summary>The median number of bullets in a spray; null without sprays.</summary>
    public int? MedianLength
    {
        get
        {
            if (Sprays == 0)
                return null;

            // Sprays that ended at bullet n are the ones that reached n but not n + 1.
            var ended = 0;
            for (var i = 0; i < Bullets.Count; i++)
            {
                ended += Bullets[i].Fired - (i + 1 < Bullets.Count ? Bullets[i + 1].Fired : 0);
                if (ended * 2 >= Sprays)
                    return Bullets[i].Bullet;
            }

            return Bullets[^1].Bullet;
        }
    }

    private SprayBullet At(int bullet) =>
        bullet >= 1 && bullet <= Bullets.Count ? Bullets[bullet - 1] : new SprayBullet(bullet, 0, 0, 0);

    /// <summary>Sums per-demo rows of one gun into one bullet per position, 1 to <paramref name="clip"/>.</summary>
    public static GunSpray From(string weapon, int clip, IEnumerable<SprayBullet> rows)
    {
        var sums = new (int Fired, int Hits, int Kills)[clip];
        foreach (var row in rows)
        {
            if (row.Bullet < 1 || row.Bullet > clip)
                continue;

            ref var s = ref sums[row.Bullet - 1];
            s = (s.Fired + row.Fired, s.Hits + row.Hits, s.Kills + row.Kills);
        }

        return new GunSpray(weapon, clip, sums.Select((s, i) => new SprayBullet(i + 1, s.Fired, s.Hits, s.Kills)).ToList());
    }
}
