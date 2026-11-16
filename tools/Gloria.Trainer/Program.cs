using System.Globalization;
using Gloria.Trainer;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

// Dev-time trainer: parses the demos in _demos/, caches per-encounter features, trains the base
// models and writes a report for tuning the rule thresholds. See docs/ml-classification.md.
var options = TrainerOptions.Parse(args);
if (options is null)
{
    Console.WriteLine("""
        Usage: dotnet run --project tools/Gloria.Trainer -- <command> [options]

        Commands:
          extract   Parse each demo and cache its encounters as CSV (skips demos already cached)
          train     Train the aim (rule-labeled) and outcome models on the cached encounters
          report    Write report.md: label rates per feature bin, threshold scans, model metrics
          publish   Copy the latest trained models to src/Gloria/models/base/
          all       extract, train, report
          reference Analyze one player in the demos of --demos into a reference the app bundles, in
                    src/Gloria/Resources/Raw/references/ (e.g. --demos _demos/donk --player donk666)

        Options:
          --demos <dir>         Demo folder (default: _demos)
          --out <dir>           Output folder (default: artifacts/training)
          --parallel <n>        Demos parsed at once (default: 2; each needs a few hundred MB)
          --force               Re-extract demos that are already cached
          --player <id|name>    reference: the player's SteamID64, or a name they played under
          --name <name>         reference: the name shown in the app (default: their latest name)
          --description <text>  reference: a line about who they are (default: the demos' maps)
        """);
    return 1;
}

try
{
    foreach (var command in options.Commands)
    {
        switch (command)
        {
            case "extract":
                await ExtractCommand.RunAsync(options);
                break;
            case "train":
                TrainCommand.Run(options);
                break;
            case "report":
                ReportCommand.Run(options);
                break;
            case "publish":
                PublishCommand.Run(options);
                break;
            case "reference":
                await ReferenceCommand.RunAsync(options);
                break;
        }
    }

    return 0;
}
catch (TrainerException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}
