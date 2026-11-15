# Aim classification with ML.NET

Gloria tags every frag and death with **aim categories**. ML.NET learns those categories from rule-based labels and from the user's corrections. Separate **outcome models** learn from what actually happened in the demos (did the first shot hit, was the duel won). This document covers what is built in `src/Gloria.Ml` and `tools/Gloria.Trainer`, how it works, and what remains.

## Categories

`Gloria.Core.Classification.AimCategory`:

| Category | Meaning | Rule in `RuleBasedAimLabeler` |
|---|---|---|
| Overshoot | The crosshair went past the target and came back | `PeakOvershootDegrees >= 1.5` |
| Undershoot | The crosshair stopped short and needed small corrections | no overshoot and `CorrectionCount >= 2` |
| MovingShot | The first shot was fired above the weapon's accurate speed (no counter-strafe) | `SpeedRatioAtFirstShot > 1` |
| Hasty | The first shot was fired before the crosshair was on the body | `AngularErrorAtFirstShot` above one body half-width at the target's distance, `atan(16 / DistanceToEnemy)` |
| SlowReaction | The first shot came late after first visibility | `TimeToFirstShot > 0.6 s` |

- An encounter can have several categories at once, for example a moving, hasty death. One with none is **clean**.
- The UI's "primary" category is the flagged category with the highest probability.
- The thresholds were checked against outcomes in the real demos; see "Training on the real demos" below. Only the Hasty tolerance changed: it used to be a fixed 2°.

## Packages

| Package | Use | Notes |
|---|---|---|
| `Microsoft.ML` 6.0.0 | `MLContext`, data views, evaluation, model save/load, feature contributions, PFI | netstandard2.0, runs on net10.0. Pure managed. |
| `Microsoft.ML.FastTree` 6.0.0 | Gradient-boosted tree binary classifiers | Managed, with an optional native speed-up. Works well with a few hundred rows. Supports per-prediction feature contributions. |
| `Microsoft.ML.LightGbm` | Alternative tree trainer | Ships a native `lib_lightgbm.dll`, x64 only, which is fine for win-x64. Not used yet; try it if FastTree gets slow on large sets. |
| `Microsoft.ML.AutoML` | Trainer and hyperparameter sweeps | Dev-time only, in the future trainer tool. Not shipped in the app. |

Not needed:
- `Microsoft.ML.TorchSharp`: deep learning on raw angle sequences would need far more labeled data than we will have.
- `Microsoft.Extensions.ML`: its `PredictionEnginePool` is built for web servers. We classify in batches instead.

## Design

### One binary model per category

ML.NET has no multi-label trainer, and the categories are not mutually exclusive. So `AimModelTrainer` trains one FastTree binary classifier per category. The pipeline for each one is:

```
Concatenate(MlEncounterRow.FeatureNames -> "Features")
  -> FastTree (label: Label, weight: Weight)
  -> CalculateFeatureContribution (top 3 positive)
```

- No normalization step: trees don't need one.
- `WeaponClass` is an ordinal code. That is fine for trees. Switch it to one-hot if a linear trainer is ever used.

### Features

- `EncounterFeatures` (Core) is the domain record. `MlEncounterRow` (Ml) flattens it into float columns.
- A missing `TimeToOnTarget` becomes `5 s` plus a `ReachedTarget = 0` flag.
- `EncounterFeatures.SchemaVersion` is stored in each model manifest. A bundle trained on another schema is refused, and the factory falls back.
- `EncounterFeatureExtractor` turns `(ParsedDemo, Encounter, EncounterMetrics)` into features. `DemoAnalyzer` runs the whole chain for every player of a demo: segmentation, metrics, features and outcome labels.
- What the features measure (`AimMetricsCalculator`):
  - Angular errors are measured to the target's body axis (hips to head), so aiming at the head or the body both count as zero error.
  - A shot is evaluated at the tick before its fire tick.
  - The **approach window** starts where the view movement that led to the first shot began, at most 1 s earlier. It does not start at first visibility, because the spotted state lags by up to half a second. Overshoot, corrections and flick distance are measured in this window. A pre-aimed shot has an empty approach.
  - `AngularErrorAtFirstVisible` and `TimeToFirstShot` are measured from the encounter start (first contact).
  - Context features (schema 2):
    - `PlayerHealth` and `OpponentHealth` are the HP one tick before the encounter start.
    - `OpponentWeaponClass` is what the opponent held one tick before the player's first shot.
    - `FiredFirst` means the opponent did not shoot at the player before the player's first shot. It looks back up to 1 s before the encounter, because missed shots are not contact.
    - Everything is read *before* the shot on purpose. The state recorded on a fire tick already includes that shot's damage, and a killed opponent holds no weapon. Reading context at the shot tick leaked the outcome: FirstShotHit AUC jumped to 0.89 on the opponent's HP and weapon, and fell back to 0.83 once fixed. `Context_IsReadBeforeTheFirstShot_SoAKillingShotCannotLeakIntoIt` guards this.

### Labels: rules first, user corrections win

- `TrainingEncounter` carries the features, the demo hash, and optional per-category corrections.
- A category without a correction is labeled by `RuleBasedAimLabeler` with weight 1.
- A corrected category uses the user's value with weight `CorrectionWeight` (default 5), through FastTree's `ExampleWeightColumnName`.
- The test `Corrections_OverrideTheRules_ForTheRegionTheyCover` shows a corrected cluster flipping the model's decision while neighboring encounters keep the rule behavior. In that test, late shots at long range are marked "not slow reaction".

**Honest limitation:** a model trained only on rule labels just reproduces the rules (the spike's models agree with them more than 95% of the time). The ML adds value only through corrections and, later, expert-labeled demos. Until a user has made enough corrections, the rules are effectively what they see.

### Evaluation

- Each category is evaluated on a split made with `TrainTestSplit(samplingKeyColumnName: DemoHash)`. All encounters from one match fall on the same side, which avoids leakage between rounds of the same game.
- AUC and F1 go into the manifest. AUC is null when the held-out demos lack positives or negatives.
- The model that is kept is then refit on all rows.
- A category whose labels are all one class is not trained, and `MlAimClassifier` uses the rules for it.

### Explainability

- `CalculateFeatureContribution` adds a `FeatureContributions` vector to each prediction. Slot *i* is `MlEncounterRow.FeatureNames[i]`.
- `MlAimClassifier` turns it into `CategoryScore.TopContributions`, so the UI can say "MovingShot, mostly because of SpeedRatioAtFirstShot".
- For global importance across a dataset, the trainer tool should report `PermutationFeatureImportance`.

### Outcome models

`OutcomeModelTrainer` trains one FastTree model per `OutcomeTarget`. The labels come from the demo, not from the rules (`OutcomeLabeler`):

| Target | Label | Features left out |
|---|---|---|
| FirstShotHit | A `player_hurt` from the player's first shot at the opponent, on its fire tick up to 2 ticks later. Null when the next shot came before the damage. | `IsKill` (it is the outcome), plus `TimeToOnTarget`, `ReachedTarget` and `ShotsWhileMovingRatio`, which are partly measured after the shot |
| WonDuel | Kill = true, death = false. Disengages and timeouts are left out. | `IsKill` |

- `OutcomeFeatures.For(target)` holds each target's feature subset, so a model cannot read the answer from its inputs.
- `MlOutcomePredictor` returns the probability and the features with **negative** contributions. Those answer "what cost this duel".
- Evaluation is leave-one-demo-out (`CrossValidation.LeaveOneGroupOut`): one fold per demo, with mean ± sd of AUC, log-loss and Brier. Grouping by demo keeps both rows of a duel (the killer's and the victim's) on the same side.
- `AimTrainingOptions.Evaluation` defaults to `Auto`, which also uses leave-one-demo-out for the aim models below 10 demos. ML.NET's `CrossValidate` hashes keys into folds, so with few demos some folds merge.
- The manifest stores each target's feature names, the fold scores and permutation feature importance (the drop in held-out AUC when a feature is shuffled).

### Storage and choosing a model

- A bundle is a folder holding `manifest.json` plus `<Category>.zip` for each trained category (`AimModelStore`). The manifest is written last, so a folder with a manifest is complete.
- The outcome bundle sits in its `outcomes/` subfolder, in the same layout (`OutcomeModelStore`).
- The base bundle is committed in `src/Gloria/models/base/` and copied to the app's output and publish folders. `ShippedBaseModelTests` fails if it no longer loads, for example after a `SchemaVersion` bump without retraining.
- `AimClassifierFactory` is the single switch point. It tries these in order:
  1. The **user model** in `%LocalAppData%\Gloria.Data\models\user\`. Like the database, this is outside the Velopack install folder, so updates keep it.
  2. The **base model** shipped in `<app>\models\base\`.
  3. `RuleBasedAimClassifier`.
- The app registers this as a lazy singleton in `MauiProgram`.

### Threading and performance

- `PredictionEngine` is not thread-safe. `MlAimClassifier.Classify` takes a batch, so call it once per imported demo, off the UI thread. It serializes calls with a lock.
- Training 5 categories on 2,000 rows (fit twice, for evaluation and the final model) takes a few seconds. That is fine for a background retrain.

### Publishing

- ML.NET relies on reflection and is not trim-safe. The MAUI Windows app is not trimmed. Keep it that way.
- The self-contained win-x64 publish includes `Microsoft.ML*.dll` (verified when the spike was added).
- The base model is included in `Gloria.csproj` as `None` items with `CopyToPublishDirectory`, so it lands in `<publish>\models\base\`.

## Feedback loop (built)

1. Import classifies every encounter and stores the scores with `ClassifierSource` (rules or a model version). `EncounterRecord.Flags` holds the effective flags: the model's (score ≥ 50%), overridden by the user's corrections.
2. The encounter screen shows the model's scores, and a "Does this look right?" card with Yes/No per category. Pressing the chosen answer again clears it.
3. Corrections are stored in `Corrections` (`CorrectionStore`). They are keyed by demo hash, player, opponent, round and first-shot tick, not by encounter id, with no foreign key: re-analysis deletes and re-creates encounters, and the importer re-applies the corrections (`CorrectionLookup`).
4. `UserModelTraining` (in the app) retrains after 10 new corrections, on "Retrain now", or at startup if a retrain is still owed.
   - It trains on every stored encounter of every player, using `EncounterRecord.ToFeatures`. Rule labels count 1 and corrections count 5 (`AimTrainingOptions.CorrectionWeight`). Nothing is re-parsed.
   - With 23 demos, training and reclassifying take a few seconds.
5. The new bundle is promoted only if it agrees with at least as many corrections as the current classifier (`Agreement`).
   - `UserModelFolder.Promote` writes it to `models/user/<version>/`, then switches the `current` file, and keeps one previous version.
   - `SwitchableAimClassifier` swaps it in for the whole app, and `CorrectionStore.ReclassifyAsync` reclassifies the stored encounters.
6. At startup, encounters classified by another source are reclassified, e.g. after an update ships a new base model.

### Limits of the loop

- **The promotion check is optimistic.** It scores the new model on the same corrected rows it was trained on, at 5× weight. FastTree memorizes them: one correction can move that encounter's score to 100%. The check catches a broken retrain, but not overfitting.
  - A fairer check would hold out each correction in turn (leave-one-correction-out), or use k folds once there are enough corrections.
- **One user model for the app,** trained on every profile's corrections. Profiles only filter what is shown.
- **The retrained model generalizes from little data.** Ten corrections against ~6,400 rule labels mostly change the corrected encounters and their close neighbors. Whether it learns the user's judgment is untested; the hand-labeled sample in the next steps would show it.
- **Retraining isn't serialized with imports.** A demo imported while a model is promoted can keep the old model's scores until the next startup catches it up.

## Training on the real demos

`tools/Gloria.Trainer` turns the demos in `_demos/` into models:

```
dotnet run --project tools/Gloria.Trainer -c Release -- all       # extract, train, report
dotnet run --project tools/Gloria.Trainer -c Release -- publish   # copy the latest bundle to src/Gloria/models/base/
```

- **extract** parses each demo and caches one CSV row per encounter in `artifacts/training/features/`. Each file is keyed by the demo's SHA-256 and the analysis version (`p{parser}-s{segmenter}-m{metrics}-f{schema}-l{labeler}`), so a demo is re-parsed only when one of those versions is bumped. Extracting 23 demos from scratch takes about 40 s; new demos are added incrementally.
- **train** trains the aim bundle (rule labels) and the outcome bundle into `artifacts/training/models/<version>/`.
- **report** writes `artifacts/training/report.md`, with per-feature CSVs in `bins/`. It covers:
  - the dataset, including what was dropped
  - the spotted cadence
  - sub-tick alignment
  - a threshold scan for every rule constant (chi-square over candidate cuts, overall and by distance)
  - rule prevalence
  - label rates per feature bin with Wilson intervals
  - the models' leave-one-demo-out scores
- Use all 10 players of every demo: 200–350 encounters per demo, 6,438 in total from 23 demos.

### What the data showed (23 demos, 2026-11-15)

The first run used 6 demos; 17 more were added the same day. Every conclusion below held with 23 demos, and the MovingShot and Undershoot ones got stronger.

- **Outcome models.** Scores are leave-one-demo-out AUC.

  | Target | Rows | AUC | Notes |
  |---|---|---|---|
  | FirstShotHit | 6,438 (23% positive) | 0.83 ± 0.03 | Mostly crosshair error at the shot, then distance and speed. The context features add nothing here, as expected: whether a shot hits depends on the aim, not on HP. |
  | WonDuel | 4,568 (63% positive) | 0.72 ± 0.05 | Up from 0.63 before the context features. Crosshair error, who fired first, opponent HP and opponent weapon matter most. |

- **Context and duels.** Win rates by context, from the report's bins:

  | Context | Win rate |
  |---|---|
  | Player fired first | 70% (51% otherwise) |
  | HP difference ≥ +50 | 81% |
  | HP difference ≤ −50 | 48% |
  | Opponent holding a knife, grenade or C4 | 89% |
  | Opponent with a pistol | 71% |
  | Opponent with a rifle | 58% |

  `FiredFirst` is partly an outcome of the aim itself: a player who reacts faster fires first. It belongs in the WonDuel model as context, but the UI should not present it as a separate mistake.

- **Shot alignment.** The crosshair error at the tick before the fire tick predicts hits best:

  | Error measured at | AUC |
  |---|---|
  | Tick before the fire tick | 0.753 |
  | Two ticks before | 0.730 |
  | Midpoint (6 demos) | 0.700 |
  | Fire tick | 0.651 |

  The angles recorded on the fire tick already include movement after the shot.
- **Spotted cadence.** The spotted state updates about every 33 ticks (~0.5 s): set runs come in lengths of 33, 66, 99 ticks.
  - Visibility onsets, and so `TimeToFirstShot` and `SlowReaction`, are only known to within half a second.
  - `TimeToFirstShot` showed no significant relation to winning the duel.
  - True line of sight would need map geometry, for example the per-map triangle meshes that awpy uses.
- **Blind shots.** Before encounters needed visibility, 56% of them started with the shot itself and hit 11% of the time; most were spam through smokes and walls. They are now dropped as "never seen".
- **Hasty threshold.** The best fixed cut fell from 2–3.5° at close range to 0.5° at long range. In body half-widths, one cut works at every range and separates hits much better (chi-square 1,062 vs 732):
  - The current rule gives a first-shot hit rate of 39% when on the body and 6% when off it (best cut 0.75 half-widths; 1 is kept because it means "off the body").
  - The old fixed 2° gave 28% / 8%.
- **MovingSpeedRatio.** The game's threshold of 1 lowers the hit rate from 35% to 21% (error < 2°). The best cut is 1.1 (1.6 on 6 demos), so the constant stays 1, which also matches the game's accuracy mechanics.
- **Overshoot and corrections.** Two or more corrections lower the hit rate (23% → 17%) and the win rate (64% → 55%), and 2 is the best cut for both labels. Overshoot has only a weak link to the outcomes (chi-square ≤ 18) and is flagged on 23% of encounters; target movement probably inflates it, so it needs the most work.
- **Bias.** Deaths where the player never fired at the killer are dropped (about 60 per demo), so WonDuel is above 50%. The report counts them.
- **SlowReaction.** Still no meaningful link between `TimeToFirstShot` and winning (chi-square 15 at a 0.05 s cut, rates 67% vs 61%); the constant stays 0.6 s.

### Next steps

- More context: armor and helmet, flashes (`player_blind`), and how many opponents could see the player at once (being traded or crossfired). Each needs a `SchemaVersion` bump and a retrain.
- To judge a player's aim, compare the WonDuel prediction with and without the aim features. That separates "lost because of the situation" from "lost because of the aim".
- Better visibility than the spotted state (map geometry).
- More demos. 23 demos still give fold-to-fold AUC spreads of ±0.03–0.04, so per-demo variation, not data size, dominates.
- Hand-label a sample of encounters with the categories, to check the rule labels against expert judgment.
