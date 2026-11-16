using Gloria.Core.Classification;
using Gloria.Core.Weapons;
using Gloria.Ml;

namespace Gloria.Ml.Tests;

/// <summary>
/// Seeded random feature rows spread over fake demos. Stands in for real encounters until
/// per-tick parsing and segmentation exist; labels come from the rules in the trainer.
/// </summary>
public static class SyntheticEncounters
{
    public static List<TrainingEncounter> Generate(int count, int seed, int demoCount = 40)
    {
        var random = new Random(seed);
        var weapons = Enum.GetValues<WeaponClass>();
        return Enumerable.Range(0, count)
            .Select(i => new TrainingEncounter(Features(random, weapons), $"demo{i % demoCount:D3}"))
            .ToList();
    }

    public static List<EncounterFeatures> Features(int count, int seed)
    {
        var random = new Random(seed);
        var weapons = Enum.GetValues<WeaponClass>();
        return Enumerable.Range(0, count).Select(_ => Features(random, weapons)).ToList();
    }

    /// <summary>An encounter the rules leave clean, to vary one feature at a time.</summary>
    public static EncounterFeatures Neutral { get; } = new(
        TimeToFirstShot: 0.35f,
        TimeToOnTarget: 0.3f,
        AngularErrorAtFirstVisible: 12f,
        AngularErrorAtFirstShot: 0.6f,
        PeakOvershootDegrees: 0.3f,
        CorrectionCount: 0,
        FlickDistanceDegrees: 12f,
        SpeedRatioAtFirstShot: 0.4f,
        ShotsWhileMovingRatio: 0.1f,
        DistanceToEnemy: 1200f,
        WeaponClass: WeaponClass.Rifle,
        IsKill: true,
        PlayerHealth: 100,
        OpponentHealth: 100,
        OpponentWeaponClass: WeaponClass.Rifle,
        FiredFirst: true,
        PreAimErrorDegrees: 2f);

    private static EncounterFeatures Features(Random random, WeaponClass[] weapons)
    {
        var timeToFirstShot = Uniform(random, 0.15f, 1.2f);
        var angleAtVisible = Uniform(random, 0f, 40f);
        return new EncounterFeatures(
            TimeToFirstShot: timeToFirstShot,
            TimeToOnTarget: random.NextDouble() < 0.85 ? timeToFirstShot * Uniform(random, 0.5f, 1.3f) : null,
            AngularErrorAtFirstVisible: angleAtVisible,
            AngularErrorAtFirstShot: Square(random) * 8f,
            PeakOvershootDegrees: Square(random) * 5f,
            CorrectionCount: random.Next(0, 6),
            FlickDistanceDegrees: angleAtVisible * Uniform(random, 0.8f, 1.2f),
            SpeedRatioAtFirstShot: Uniform(random, 0f, 2f),
            ShotsWhileMovingRatio: (float)random.NextDouble(),
            DistanceToEnemy: Uniform(random, 200f, 3500f),
            WeaponClass: weapons[random.Next(weapons.Length)],
            IsKill: random.NextDouble() < 0.5,
            PlayerHealth: random.NextDouble() < 0.6 ? 100 : random.Next(1, 100),
            OpponentHealth: random.NextDouble() < 0.6 ? 100 : random.Next(1, 100),
            OpponentWeaponClass: weapons[random.Next(weapons.Length)],
            FiredFirst: random.NextDouble() < 0.5,
            PreAimErrorDegrees: Uniform(random, 0f, 60f));
    }

    private static float Uniform(Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);

    // Skews towards small values, like real aim errors.
    private static float Square(Random random) => (float)Math.Pow(random.NextDouble(), 2);
}

public sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gloria-ml-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}

/// <summary>Trains once on rule labels and shares the bundle across tests.</summary>
public sealed class TrainedBundleFixture
{
    public AimModelBundle Bundle { get; } = new AimModelTrainer().Train(SyntheticEncounters.Generate(2000, seed: 7));
}
