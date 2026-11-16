using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

// Renders the agent models into the sprites the replay draws: one PNG per team, pose and yaw step.
//
//   dotnet run --project tools/Gloria.Sprites -- [--models _assets/models] [--out src/Gloria/Resources/Raw/agents] [--step 10]
//
// The models folder holds ct.glb and t.glb (downloaded by hand from Sketchfab; not in git). agents.json next to
// this file can set, per team, "file", "yawOffset" (degrees, so yaw 0 faces the camera), an animation
// "clip"/"clipTime" to pose from, and "stand"/"crouch" bone rotations ({ "boneName": [x, y, z] } in degrees).
// Without a crouch pose the standing model is squashed to crouch height. The bone and clip names are printed
// on each run, and a contact sheet per team goes to artifacts/sprites/ to check the result.
var root = FindRoot();
string Arg(string name, string fallback) =>
    args.SkipWhile(a => a != name).Skip(1).FirstOrDefault() ?? fallback;
var modelsDir = Path.GetFullPath(Arg("--models", Path.Combine(root, "_assets", "models")));
var outDir = Path.GetFullPath(Arg("--out", Path.Combine(root, "src", "Gloria", "Resources", "Raw", "agents")));
var sheetDir = Path.Combine(root, "artifacts", "sprites");
var step = int.Parse(Arg("--step", "10"));
var channel = Arg("--channel", "msedge");

// The pose settings are committed next to the tool so renders can be repeated; --config overrides them.
var configPath = Arg("--config", Path.Combine(root, "tools", "Gloria.Sprites", "agents.json"));
var config = File.Exists(configPath) ? JsonNode.Parse(File.ReadAllText(configPath))!.AsObject() : [];
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(sheetDir);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = channel, Args = ["--use-angle=swiftshader", "--enable-unsafe-swiftshader"] });
var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 256, Height = 256 } });
page.Console += (_, message) => Console.WriteLine($"  [page] {message.Text}");

// Serve the page and the models from a made-up origin, so module imports and fetches work.
await page.RouteAsync("http://gloria.sprites/**", async route =>
{
    var path = new Uri(route.Request.Url).AbsolutePath.TrimStart('/');
    var file = path == "render.html" ? Path.Combine(AppContext.BaseDirectory, "render.html") : Path.Combine(modelsDir, path);
    if (!File.Exists(file))
    {
        await route.FulfillAsync(new() { Status = 404 });
        return;
    }

    await route.FulfillAsync(new() { BodyBytes = await File.ReadAllBytesAsync(file), ContentType = path.EndsWith(".html") ? "text/html" : "model/gltf-binary" });
});

JsonElement? frame = null;
var rendered = 0;
foreach (var team in new[] { "ct", "t" })
{
    var options = config[team]?.AsObject() ?? [];
    var file = options["file"]?.GetValue<string>() ?? $"{team}.glb";
    if (!File.Exists(Path.Combine(modelsDir, file)))
    {
        Console.WriteLine($"{team}: no {Path.Combine(modelsDir, file)}, skipped");
        continue;
    }

    await page.GotoAsync("http://gloria.sprites/render.html");
    await page.WaitForFunctionAsync("window.ready === true");
    var info = await page.EvaluateAsync<JsonElement>(
        "([url, config]) => window.setup(url, JSON.parse(config))", new object[] { $"http://gloria.sprites/{file}", options.ToJsonString() });
    Console.WriteLine($"{team}: {file}, {info.GetProperty("height").GetDouble():0.##} model units tall");
    Console.WriteLine($"  clips: {string.Join(", ", info.GetProperty("clips").EnumerateArray().Select(c => c.GetString()))}");
    Console.WriteLine($"  bones: {string.Join(", ", info.GetProperty("bones").EnumerateArray().Select(b => b.GetString()))}");

    var sheet = new List<(string Label, byte[] Png)>();
    foreach (var pose in new[] { "stand", "crouch" })
    {
        var bones = options[pose];
        double? squash = pose == "crouch" && bones is null ? 54.0 / 72 : null;
        for (var yaw = 0; yaw < 360; yaw += step)
        {
            frame = await page.EvaluateAsync<JsonElement>(
                "([yaw, pose, squash]) => window.render(yaw, pose ? JSON.parse(pose) : null, squash)",
                new object?[] { yaw, bones?.ToJsonString(), squash });
            if (yaw == 0 && frame.Value.TryGetProperty("height", out var height) && height.ValueKind == JsonValueKind.Number)
                Console.WriteLine($"  {pose}: {height.GetDouble():0} units tall");
            var png = await page.Locator("canvas").ScreenshotAsync(new() { OmitBackground = true });
            await File.WriteAllBytesAsync(Path.Combine(outDir, $"{team}_{pose}_{yaw:000}.png"), png);
            sheet.Add(($"{pose} {yaw}°", png));
            rendered++;
        }
    }

    await WriteSheetAsync(browser, sheet, Path.Combine(sheetDir, $"{team}.png"));
}

if (frame is null)
{
    Console.WriteLine($"Nothing rendered: put ct.glb and t.glb in {modelsDir}.");
    return 1;
}

// What the replay needs to place a frame to scale: the units the image spans around the feet.
var meta = new JsonObject
{
    ["step"] = step,
    ["bottom"] = frame.Value.GetProperty("bottom").GetDouble(),
    ["top"] = frame.Value.GetProperty("top").GetDouble(),
    ["halfWidth"] = frame.Value.GetProperty("halfWidth").GetDouble(),
};
await File.WriteAllTextAsync(Path.Combine(outDir, "agents.json"), meta.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{rendered} sprites in {outDir}; contact sheets in {sheetDir}");
return 0;

static async Task WriteSheetAsync(IBrowser browser, List<(string Label, byte[] Png)> frames, string path)
{
    var cells = string.Concat(frames.Select(f =>
        $"<figure><img src=\"data:image/png;base64,{Convert.ToBase64String(f.Png)}\"><figcaption>{f.Label}</figcaption></figure>"));
    var page = await browser.NewPageAsync(new() { ViewportSize = new() { Width = 9 * 132, Height = 200 } });
    await page.SetContentAsync(
        "<style>body{margin:0;background:#111316;color:#8b8f97;font:11px sans-serif;display:flex;flex-wrap:wrap}"
        + "figure{margin:2px;width:128px;text-align:center}img{width:128px;height:128px}</style>" + cells);
    await page.ScreenshotAsync(new() { Path = path, FullPage = true });
    await page.CloseAsync();
}

static string FindRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Gloria.slnx")))
            return dir.FullName;
    }

    return Directory.GetCurrentDirectory();
}
