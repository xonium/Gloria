using System.Text.Json;
using System.Text.Json.Serialization;
using Gloria.Core.Encounters;
using Gloria.Core.Metrics;
using Gloria.Data.Entities;

namespace Gloria.Data.References;

/// <param name="Rounds">Live rounds the player played.</param>
public sealed record ReferenceDemo(string Map, int Rounds);

/// <summary>
/// A player bundled with the app to compare against, e.g. a pro: their encounters' metrics from a few demos,
/// without traces and without the demos. Made by <c>tools/Gloria.Trainer reference</c>; the app classifies the
/// encounters with its own aim model when it loads them, so their habits are judged like the user's.
/// </summary>
/// <param name="AnalysisVersion">The analysis that measured the encounters (<c>DemoImporter.AnalysisVersionKey</c>).</param>
public sealed record ReferencePlayer(
    int Format,
    string Name,
    ulong SteamId,
    string Description,
    string AnalysisVersion,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ReferenceDemo> Demos,
    IReadOnlyList<ReferenceEncounter> Encounters)
{
    public const int CurrentFormat = 1;

    /// <summary>The list of reference files in the app's references folder, since app package folders can't be listed.</summary>
    public const string IndexFileName = "index.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ReferencePlayer Read(Stream stream) =>
        JsonSerializer.Deserialize<ReferencePlayer>(stream, Json) ?? throw new InvalidDataException("Empty reference file.");

    public void Write(Stream stream) => JsonSerializer.Serialize(stream, this, Json);

    public static IReadOnlyList<string> ReadIndex(Stream stream) =>
        JsonSerializer.Deserialize<List<string>>(stream, Json) ?? [];

    public static void WriteIndex(Stream stream, IReadOnlyList<string> files) => JsonSerializer.Serialize(stream, files, Json);

    /// <summary>The encounters as records, unclassified (no flags), numbered from 1 so they have distinct ids.</summary>
    public IReadOnlyList<EncounterRecord> ToRecords() => Encounters
        .Select((e, i) => e.ToRecord(i + 1, SteamId))
        .ToList();
}

/// <summary>What an <see cref="EncounterRecord"/> holds for the stats and the models; no ticks, trace or classification.</summary>
/// <param name="Demo">Index into <see cref="ReferencePlayer.Demos"/>.</param>
public sealed record ReferenceEncounter(
    int Demo,
    int Round,
    EncounterOutcome Outcome,
    AimApproach Approach,
    double? TimeToFirstShot,
    double? TimeToOnTarget,
    string FirstShotWeapon,
    string OpponentWeapon,
    int ShotCount,
    int ShotsWhileMoving,
    float SpeedAtFirstShot,
    float SpeedRatioAtFirstShot,
    float AngularErrorAtStart,
    float AngularErrorAtFirstShot,
    float PeakOvershootDegrees,
    int CorrectionCount,
    float FlickDistanceDegrees,
    float? PreAimErrorDegrees,
    float DistanceAtFirstShot,
    int PlayerHealthAtStart,
    int OpponentHealthAtStart,
    bool FiredFirst,
    bool? FirstShotHit,
    bool? WonDuel)
{
    public static ReferenceEncounter From(EncounterRecord e, int demo) => new(
        demo,
        e.RoundNumber,
        e.Outcome,
        e.Approach,
        e.TimeToFirstShot,
        e.TimeToOnTarget,
        e.FirstShotWeapon,
        e.OpponentWeapon,
        e.ShotCount,
        e.ShotsWhileMoving,
        e.SpeedAtFirstShot,
        e.SpeedRatioAtFirstShot,
        e.AngularErrorAtStart,
        e.AngularErrorAtFirstShot,
        e.PeakOvershootDegrees,
        e.CorrectionCount,
        e.FlickDistanceDegrees,
        e.PreAimErrorDegrees,
        e.DistanceAtFirstShot,
        e.PlayerHealthAtStart,
        e.OpponentHealthAtStart,
        e.FiredFirst,
        e.FirstShotHit,
        e.WonDuel);

    /// <param name="id">Stands in for a database id; the demo index is the <see cref="EncounterRecord.DemoId"/>.</param>
    public EncounterRecord ToRecord(int id, ulong steamId) => new()
    {
        Id = id,
        DemoId = Demo,
        PlayerSteamId = steamId,
        RoundNumber = Round,
        Outcome = Outcome,
        Approach = Approach,
        TimeToFirstShot = TimeToFirstShot,
        TimeToOnTarget = TimeToOnTarget,
        FirstShotWeapon = FirstShotWeapon,
        OpponentWeapon = OpponentWeapon,
        ShotCount = ShotCount,
        ShotsWhileMoving = ShotsWhileMoving,
        SpeedAtFirstShot = SpeedAtFirstShot,
        SpeedRatioAtFirstShot = SpeedRatioAtFirstShot,
        AngularErrorAtStart = AngularErrorAtStart,
        AngularErrorAtFirstShot = AngularErrorAtFirstShot,
        PeakOvershootDegrees = PeakOvershootDegrees,
        CorrectionCount = CorrectionCount,
        FlickDistanceDegrees = FlickDistanceDegrees,
        PreAimErrorDegrees = PreAimErrorDegrees,
        DistanceAtFirstShot = DistanceAtFirstShot,
        PlayerHealthAtStart = PlayerHealthAtStart,
        OpponentHealthAtStart = OpponentHealthAtStart,
        FiredFirst = FiredFirst,
        FirstShotHit = FirstShotHit,
        WonDuel = WonDuel,
    };
}
