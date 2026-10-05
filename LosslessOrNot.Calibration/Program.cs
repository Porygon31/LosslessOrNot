using System.Collections.Concurrent;
using System.Globalization;
using LosslessOrNot.Core;
using LosslessOrNot.Core.Media;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot.Calibration;

/// <summary>
/// Fits the weights of the probability on genuine lossless files and the fakes made
/// from them: every original is analysed, then transcoded with real encoders and
/// decoded back into FLAC, and every fake analysed in turn. Nothing leaves the
/// machine: the transcodes are deleted as soon as they are measured.
/// </summary>
internal static class Program
{
    private const string Usage =
        """
        Usage : LosslessOrNot.Calibration <dossier de vrais lossless> [options]

          --max-files N   nombre de morceaux tirés au hasard (60 par défaut)
          --jobs N        morceaux traités en parallèle (moitié des cœurs par défaut)
          --window S      secondes analysées par morceau (45 par défaut, comme l'application)
          --fft N         points par trame FFT, puissance de 2 de 2048 à 16384 (4096 par défaut, comme l'application)
          --out DOSSIER   où écrire features.csv et report.md (calibration-<date> par défaut)
          --seed N        graine du tirage (1 par défaut)
          --ffmpeg CHEMIN dossier de ffmpeg et ffprobe (sinon dossier de l'application, puis PATH)
        """;

    private static async Task<int> Main(string[] args)
    {
        if (!TryParse(args, out Options options))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }
        FfmpegTools? tools = FfmpegLocator.Locate(options.Ffmpeg);
        if (tools is null)
        {
            Console.Error.WriteLine("ffmpeg et ffprobe sont introuvables (option --ffmpeg, dossier de l'application ou PATH).");
            return 1;
        }

        var transcoder = new Transcoder(tools);
        HashSet<string> encoders = await transcoder.EncodersAsync();
        Recipe[] recipes = Recipe.All.Where(r => encoders.Contains(r.Encoder)).ToArray();
        string[] missing = Recipe.All.Select(r => r.Encoder).Distinct().Where(e => !encoders.Contains(e)).ToArray();
        if (missing.Length > 0)
        {
            Console.WriteLine($"Encodeurs absents, recettes ignorées : {string.Join(", ", missing)}");
        }

        Console.WriteLine($"Recherche de vrais lossless dans {options.Folder}…");
        List<string> sources = await PickSourcesAsync(options, tools);
        if (sources.Count == 0)
        {
            Console.Error.WriteLine("Aucun fichier lossless trouvé.");
            return 1;
        }
        Console.WriteLine(
            $"{sources.Count} morceaux, {recipes.Length} transcodages chacun, {options.Jobs} en parallèle ; "
            + $"{options.WindowSec.ToString(CultureInfo.InvariantCulture)} s analysées en trames FFT de {options.FftSize} points.");
        Console.WriteLine("Ctrl+C arrête proprement : les transcodages temporaires sont supprimés et les résultats partiels écrits.");

        // Ctrl+C stops the runs in progress, which delete their temporary files, and
        // whatever was measured so far is still written.
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        var rows = new ConcurrentBag<Row>();
        int done = 0, failures = 0;
        using var slots = new SemaphoreSlim(options.Jobs);
        await Task.WhenAll(sources.Select(async (path, group) =>
        {
            try
            {
                await slots.WaitAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            try
            {
                List<Row> measured = await CalibrateAsync(
                    path, group, options, tools, transcoder, recipes, () => Interlocked.Increment(ref failures), stop.Token);
                foreach (Row row in measured)
                {
                    rows.Add(row);
                }
                if (!stop.IsCancellationRequested)
                {
                    Console.WriteLine($"[{Interlocked.Increment(ref done)}/{sources.Count}] {Summary(Path.GetRelativePath(options.Folder, path), measured)}");
                }
            }
            finally
            {
                slots.Release();
            }
        }));
        if (stop.IsCancellationRequested)
        {
            Console.WriteLine($"Interrompu après {done} morceaux : résultats partiels.");
        }

        Row[] ordered = rows.OrderBy(r => r.Group).ThenBy(r => r.IsTranscode).ThenBy(r => r.Recipe, StringComparer.Ordinal).ToArray();
        if (!ordered.Any(r => r.IsTranscode) || !ordered.Any(r => !r.IsTranscode))
        {
            Console.Error.WriteLine("Il faut des vrais lossless et des transcodages analysés pour ajuster les poids.");
            return 1;
        }
        Directory.CreateDirectory(options.Out);
        string csv = Path.Combine(options.Out, "features.csv");
        await File.WriteAllLinesAsync(csv, [Row.CsvHeader, .. ordered.Select(r => r.ToCsv())]);

        // Transcodes without any wall where encoders cut look exactly like their
        // original: they would only teach the fit to doubt every genuine file.
        FitResult fit = LogisticFit.FitWithCrossValidation(ordered
            .Where(r => !r.IsTranscode || r.HasCodecCandidate)
            .Select(r => new Sample(r.Contributions, r.IsTranscode, r.Group))
            .ToArray());
        string report = Path.Combine(options.Out, "report.md");
        await File.WriteAllTextAsync(
            report, Report.Write(ordered, fit, options.Folder, options.WindowSec, options.FftSize, missing, failures));

        Console.WriteLine();
        Console.WriteLine($"Mesures : {Path.GetFullPath(csv)}");
        Console.WriteLine($"Rapport : {Path.GetFullPath(report)}");
        return 0;
    }

    /// <summary>The original, then each of its fakes; files that fail are reported and skipped.</summary>
    private static async Task<List<Row>> CalibrateAsync(
        string path,
        int group,
        Options options,
        FfmpegTools tools,
        Transcoder transcoder,
        Recipe[] recipes,
        Action failed,
        CancellationToken cancellationToken)
    {
        var rows = new List<Row>();
        string source = Path.GetRelativePath(options.Folder, path);
        var analyzer = new TrackAnalyzer(tools, new AnalysisOptions(options.WindowSec, options.FftSize));
        TrackResult original;
        try
        {
            original = await analyzer.AnalyzeAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return rows;
        }
        if (original.Spectral is null || original.Info is not { } info)
        {
            Console.Error.WriteLine($"{source} : {original.Error}");
            failed();
            return rows;
        }
        rows.Add(Row.From(group, source, "genuine", false, original));

        DirectoryInfo temp = Directory.CreateTempSubdirectory("losslessornot-calibration-");
        try
        {
            string excerpt = Path.Combine(temp.FullName, "excerpt.wav");
            double start = SpectralAnalyzer.AnalysisStartSec(info.DurationSec, original.Spectral.RequestedSec);
            await transcoder.ExtractAsync(path, start, original.Spectral.RequestedSec, info.BitsPerSample, excerpt, cancellationToken);
            foreach (Recipe recipe in recipes.Where(r => r.Applies(info.SampleRate)))
            {
                try
                {
                    string fake = await transcoder.MakeAsync(recipe, excerpt, info.SampleRate, temp.FullName, cancellationToken);
                    TrackResult result = await analyzer.AnalyzeAsync(fake, cancellationToken);
                    File.Delete(fake);
                    if (result.Spectral is null)
                    {
                        throw new AudioAnalysisException(result.Error ?? "analyse impossible");
                    }
                    rows.Add(Row.From(group, source, recipe.Name, true, result));
                }
                catch (AudioAnalysisException ex)
                {
                    Console.Error.WriteLine($"{source} [{recipe.Name}] : {ex.Message}");
                    failed();
                }
            }
        }
        catch (AudioAnalysisException ex)
        {
            Console.Error.WriteLine($"{source} : {ex.Message}");
            failed();
        }
        catch (OperationCanceledException)
        {
            // Stopped: keep what was measured.
        }
        finally
        {
            temp.Delete(recursive: true);
        }
        return rows;
    }

    /// <summary>A seeded random draw among the lossless files of the folder, ffprobe having the final word.</summary>
    private static async Task<List<string>> PickSourcesAsync(Options options, FfmpegTools tools)
    {
        string[] candidates = Directory
            .EnumerateFiles(options.Folder, "*", SearchOption.AllDirectories)
            .Where(AudioFiles.HasAudioExtension)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        new Random(options.Seed).Shuffle(candidates);
        var picked = new List<string>();
        foreach (string candidate in candidates)
        {
            if (picked.Count >= options.MaxFiles)
            {
                break;
            }
            try
            {
                AudioInfo info = await AudioProbe.ProbeAsync(tools.FfprobePath, candidate);
                if (info.IsLosslessCodec && info.DurationSec >= 10)
                {
                    picked.Add(candidate);
                }
            }
            catch (AudioAnalysisException)
            {
                // Unreadable: not part of the corpus.
            }
        }
        return picked;
    }

    private static string Summary(string source, List<Row> rows)
    {
        Row? genuine = rows.FirstOrDefault(r => !r.IsTranscode);
        int fakes = rows.Count(r => r.IsTranscode);
        int caught = rows.Count(r => r.IsTranscode && r.Status == Core.Assessment.LosslessStatus.FakeLossless);
        return genuine is null
            ? $"{source} : échec"
            : $"{source} : original {genuine.Status} (P {genuine.LossyProbability:P0}), transcodages détectés {caught}/{fakes}";
    }

    private sealed record Options(
        string Folder, int MaxFiles, int Jobs, double WindowSec, int FftSize, string Out, int Seed, string? Ffmpeg);

    private static bool TryParse(string[] args, out Options options)
    {
        options = new Options(
            "",
            60,
            Math.Max(1, Environment.ProcessorCount / 2),
            SpectralAnalyzer.DefaultWindowSec,
            SpectralAnalyzer.DefaultFftSize,
            $"calibration-{DateTime.Now:yyyyMMdd-HHmmss}",
            1,
            null);
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;
            switch (arg)
            {
                case "--max-files" when int.TryParse(Next(), out int n) && n > 0:
                    options = options with { MaxFiles = n };
                    break;
                case "--jobs" when int.TryParse(Next(), out int n) && n > 0:
                    options = options with { Jobs = n };
                    break;
                case "--window" when double.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out double s):
                    options = options with { WindowSec = Math.Clamp(s, SpectralAnalyzer.MinWindowSec, SpectralAnalyzer.MaxWindowSec) };
                    break;
                case "--fft" when int.TryParse(Next(), out int n) && SpectralAnalyzer.IsValidFftSize(n):
                    options = options with { FftSize = n };
                    break;
                case "--out" when Next() is { } folder:
                    options = options with { Out = folder };
                    break;
                case "--seed" when int.TryParse(Next(), out int n):
                    options = options with { Seed = n };
                    break;
                case "--ffmpeg" when Next() is { } ffmpeg:
                    options = options with { Ffmpeg = ffmpeg };
                    break;
                case not null when !arg.StartsWith("--", StringComparison.Ordinal) && options.Folder.Length == 0:
                    options = options with { Folder = Path.GetFullPath(arg) };
                    break;
                default:
                    return false;
            }
        }
        return options.Folder.Length > 0 && Directory.Exists(options.Folder);
    }
}
