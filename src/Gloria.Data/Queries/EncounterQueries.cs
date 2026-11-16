using Gloria.Data.Entities;

namespace Gloria.Data.Queries;

public static class EncounterQueries
{
    /// <summary>
    /// Encounters with the columns <see cref="EncounterRecord.ToFeatures"/> needs, but without the trace, which is
    /// most of a row's size. The results are not tracked.
    /// </summary>
    public static IQueryable<EncounterRecord> ForFeatures(this IQueryable<EncounterRecord> encounters) =>
        encounters.Select(e => new EncounterRecord
        {
            Id = e.Id,
            DemoId = e.DemoId,
            PlayerSteamId = e.PlayerSteamId,
            OpponentSteamId = e.OpponentSteamId,
            RoundNumber = e.RoundNumber,
            Outcome = e.Outcome,
            TimeToFirstShot = e.TimeToFirstShot,
            TimeToOnTarget = e.TimeToOnTarget,
            FirstShotTick = e.FirstShotTick,
            FirstShotWeapon = e.FirstShotWeapon,
            OpponentWeapon = e.OpponentWeapon,
            ShotCount = e.ShotCount,
            ShotsWhileMoving = e.ShotsWhileMoving,
            SpeedRatioAtFirstShot = e.SpeedRatioAtFirstShot,
            AngularErrorAtStart = e.AngularErrorAtStart,
            AngularErrorAtFirstShot = e.AngularErrorAtFirstShot,
            PeakOvershootDegrees = e.PeakOvershootDegrees,
            CorrectionCount = e.CorrectionCount,
            FlickDistanceDegrees = e.FlickDistanceDegrees,
            PreAimErrorDegrees = e.PreAimErrorDegrees,
            DistanceAtFirstShot = e.DistanceAtFirstShot,
            PlayerHealthAtStart = e.PlayerHealthAtStart,
            OpponentHealthAtStart = e.OpponentHealthAtStart,
            FiredFirst = e.FiredFirst,
        });
}
