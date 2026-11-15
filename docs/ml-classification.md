# Aim classification with ML.NET

Gloria tags every frag and death with **aim categories**. ML.NET learns those categories from rule-based labels and from the user's corrections. This document covers what is built (the spike in `src/Gloria.Ml`), how it works, and what remains.

## Categories

`Gloria.Core.Classification.AimCategory`:

| Category | Meaning | Rule in `RuleBasedAimLabeler` (initial guess) |
|---|---|---|
| Overshoot | The crosshair went past the target and came back | `PeakOvershootDegrees >= 1.5` |
| Undershoot | The crosshair stopped short and needed small corrections | no overshoot and `CorrectionCount >= 2` |
| MovingShot | The first shot was fired above the weapon's accurate speed (no counter-strafe) | `SpeedRatioAtFirstShot > 1` |
| Hasty | The first shot was fired before the crosshair was on target | `AngularErrorAtFirstShot > 2°` |
| SlowReaction | The first shot came late after first visibility | `TimeToFirstShot > 0.6 s` |

- An encounter can have several categories at once, for example a moving, hasty death. One with none is **clean**.
- The UI's "primary" category is the flagged category with the highest probability.
- The thresholds are placeholders until they are tuned on real demos. The fixed 2° on-target tolerance should eventually depend on distance, because the target's angular size shrinks with range.

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
- `IEncounterFeatureExtractor` turns `(ParsedDemo, Encounter, EncounterMetrics)` into features. It is not implemented yet: it needs per-tick state, the segmenter and the metrics calculator.

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

### Storage and choosing a model

- A bundle is a folder holding `manifest.json` plus `<Category>.zip` for each trained category (`AimModelStore`). The manifest is written last, so a folder with a manifest is complete.
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
- The base model will be copied into the publish output as content (see below), not into `bin/`.

## Not built yet

### Feedback loop

1. Import a demo, extract features, classify, and store the labels with `Source` = rules or the model version.
2. Show each encounter's categories and their top contributions in the UI.
3. Let the user confirm or correct a category, and store it as a `User` label.
4. After N new corrections, retrain in the background on all stored features: rule labels plus weighted corrections.
5. Promote the new bundle only if it does at least as well as the current one on the corrected rows. Write it to a new folder, then switch, so a failed retrain never leaves a broken model.

### Data schema (a future migration in `Gloria.Data`)

- `EncounterRecord`: demo, round, opponent, start and end ticks, outcome.
- `EncounterFeatureRecord`: `EncounterId`, `SchemaVersion`, and the feature columns. These are the cached training input, so retraining needs no re-parsing.
- `EncounterLabelRecord`: `(EncounterId, Category, Source = Rule|User|Model, Value, Probability, ModelVersion)`.
- Re-classification when the model changes ties into `DemoRecord.AnalysisVersion`.

### Dev-time trainer (`tools/Gloria.Trainer`)

A console app that:
1. Parses `_demos/` and extracts features to CSV.
2. Optionally runs an AutoML sweep to compare FastTree, LightGBM and SDCA.
3. Trains the base bundle and writes AUC/F1 and PFI reports.
4. Copies the bundle to `src/Gloria/models/base/`, and the app csproj includes that folder as content.

### Order of work

Per-tick state in `DemoFileDemoParser` → `IEncounterSegmenter` → `IAimMetricsCalculator` → `IEncounterFeatureExtractor` → stored encounters, features and labels → correction UI → background retraining → trainer tool and the shipped base model.
