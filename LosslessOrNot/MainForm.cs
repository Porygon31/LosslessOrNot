using System.ComponentModel;
using System.Diagnostics;
using LosslessOrNot.Charts;
using LosslessOrNot.Core;
using LosslessOrNot.Core.Assessment;
using LosslessOrNot.Core.Media;
using LosslessOrNot.Core.Spectral;

namespace LosslessOrNot;

public partial class MainForm : Form
{
    // Extensions picked up when a folder is added. They only decide what is worth
    // probing: ffprobe has the final word on what a file really contains.
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".wav", ".aif", ".aiff", ".aifc", ".m4a", ".alac", ".ape", ".wv", ".tta",
        ".mp3", ".aac", ".ogg", ".oga", ".opus", ".wma",
    };

    private const string AudioFileFilter =
        "Fichiers audio|*.flac;*.wav;*.aif;*.aiff;*.aifc;*.m4a;*.alac;*.ape;*.wv;*.tta;*.mp3;*.aac;*.ogg;*.oga;*.opus;*.wma"
        + "|Tous les fichiers|*.*";

    private readonly string[] _startupPaths;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Dictionary<TrackEntry, ListViewItem> _items = [];
    private readonly HashSet<string> _knownPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Font _headingFont;
    private FfmpegTools? _tools;

    // Set while an analysis runs; there is never more than one run at a time.
    private CancellationTokenSource? _run;
    private bool _restartWhenIdle;
    private int _runTotal;
    private int _runDone;

    // The entry shown in the detail panel.
    private TrackEntry? _shown;
    private bool _selectionRefreshPending;

    public MainForm()
        : this([])
    {
    }

    /// <param name="startupPaths">Files or folders to add and analyse as soon as the window shows.</param>
    public MainForm(string[] startupPaths)
    {
        InitializeComponent();
        _startupPaths = startupPaths;
        _headingFont = new Font(detailsBox.Font, FontStyle.Bold);
        paletteComboBox.SelectedIndex = Enum.IsDefined(_settings.Palette) ? (int)_settings.Palette : 0;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Proportions rather than designer pixels, so the layout holds at any DPI.
        SetSplitterRatio(mainSplit, 0.42);
        SetSplitterRatio(detailSplit, 0.36);

        _tools = FfmpegLocator.Locate(_settings.FfmpegPath);
        UpdateFfmpegStatus();
        SetBusy(false);
        ShowDetails(null);
        UpdateSummary();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_startupPaths.Length > 0)
        {
            await AddPathsAsync(_startupPaths);
        }
        else if (_tools is null)
        {
            await AskForFfmpegAsync();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Kills the ffmpeg processes still decoding.
        _run?.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _headingFont.Dispose();
        base.OnFormClosed(e);
    }

    private static void SetSplitterRatio(SplitContainer split, double ratio)
    {
        int size = split.Orientation == Orientation.Horizontal ? split.Height : split.Width;
        int max = size - split.Panel2MinSize - split.SplitterWidth;
        if (max > split.Panel1MinSize)
        {
            split.SplitterDistance = Math.Clamp((int)(size * ratio), split.Panel1MinSize, max);
        }
    }

    // ── Adding files ─────────────────────────────────────────────────────────

    private async void addFilesButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Ajouter des fichiers audio",
            Filter = AudioFileFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    private async void addFolderButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choisir un dossier à analyser (sous-dossiers compris)",
            UseDescriptionForTitle = true,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            await AddPathsAsync([dialog.SelectedPath]);
        }
    }

    private void files_DragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private async void files_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await AddPathsAsync(paths);
        }
    }

    /// <summary>Adds the audio files among <paramref name="paths"/> (folders searched recursively), then analyses them.</summary>
    private async Task AddPathsAsync(IReadOnlyCollection<string> paths)
    {
        summaryLabel.Text = "Recherche des fichiers audio…";
        // Off the UI thread: a large or networked music folder can take a while to list.
        List<string> files = await Task.Run(() => CollectAudioFiles(paths));

        var items = new List<ListViewItem>();
        foreach (string file in files)
        {
            if (!_knownPaths.Add(file))
            {
                continue;
            }
            var entry = new TrackEntry(file);
            var item = new ListViewItem(entry.FileName)
            {
                Tag = entry,
                ToolTipText = entry.Path,
                UseItemStyleForSubItems = false,
            };
            for (int i = 1; i < fileList.Columns.Count; i++)
            {
                item.SubItems.Add("");
            }
            _items[entry] = item;
            items.Add(item);
            UpdateItem(entry);
        }

        fileList.BeginUpdate();
        try
        {
            fileList.Items.AddRange(items.ToArray());
        }
        finally
        {
            fileList.EndUpdate();
        }

        if (items.Count == 0)
        {
            UpdateSummary(files.Count == 0 ? "Aucun fichier audio trouvé" : "Ces fichiers sont déjà dans la liste");
            return;
        }
        UpdateSummary();
        await RunAnalysisAsync();
    }

    private static List<string> CollectAudioFiles(IEnumerable<string> paths)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        var files = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                string fullPath = Path.GetFullPath(path);
                if (Directory.Exists(fullPath))
                {
                    files.AddRange(Directory.EnumerateFiles(fullPath, "*", options)
                        .Where(IsAudioFile)
                        .Order(StringComparer.CurrentCultureIgnoreCase));
                }
                else if (File.Exists(fullPath) && IsAudioFile(fullPath))
                {
                    files.Add(fullPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // An unreadable path is skipped; the rest of the drop still counts.
            }
        }
        return files;
    }

    private static bool IsAudioFile(string path) => AudioExtensions.Contains(Path.GetExtension(path));

    // ── Analysis ─────────────────────────────────────────────────────────────

    private async void analyzeButton_Click(object? sender, EventArgs e)
    {
        if (!_items.Keys.Any(entry => entry.State == TrackState.Pending))
        {
            summaryLabel.Text = _items.Count == 0
                ? "Ajoutez d'abord des fichiers audio."
                : "Aucun fichier en attente. Pour relancer une analyse : clic droit › Réanalyser.";
            return;
        }
        await RunAnalysisAsync();
    }

    private void cancelButton_Click(object? sender, EventArgs e) => _run?.Cancel();

    /// <summary>
    /// Analyses every pending entry — and those added meanwhile — until none is left
    /// or the user cancels.
    /// </summary>
    private async Task RunAnalysisAsync()
    {
        if (_run is not null)
        {
            // The running batch picks new entries up once its current ones are done —
            // unless it is being cancelled, in which case start afresh after it.
            _restartWhenIdle |= _run.IsCancellationRequested;
            return;
        }
        if (_tools is null)
        {
            if (_items.Count > 0)
            {
                await AskForFfmpegAsync();
            }
            return;
        }

        using var run = new CancellationTokenSource();
        _run = run;
        _runTotal = 0;
        _runDone = 0;
        SetBusy(true);
        var analyzer = new TrackAnalyzer(_tools);
        IProgress<(TrackEntry Entry, TrackResult? Result)> progress =
            new Progress<(TrackEntry Entry, TrackResult? Result)>(OnAnalysisProgress);
        var options = new ParallelOptions
        {
            // Each analysis is one ffmpeg decode plus an FFT pass: a few at once keep
            // the machine busy without drowning it.
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            CancellationToken = run.Token,
        };
        string? outcome = null;
        try
        {
            List<TrackEntry> batch;
            while ((batch = _items.Keys.Where(entry => entry.State == TrackState.Pending).ToList()).Count > 0)
            {
                foreach (TrackEntry entry in batch)
                {
                    entry.State = TrackState.Queued;
                }
                _runTotal += batch.Count;
                UpdateProgress();
                await Parallel.ForEachAsync(batch, options, async (entry, token) =>
                {
                    progress.Report((entry, null));
                    TrackResult result = await analyzer.AnalyzeAsync(entry.Path, token);
                    progress.Report((entry, result));
                });
            }
            outcome = "Analyse terminée";
        }
        catch (OperationCanceledException)
        {
            outcome = "Analyse annulée";
            foreach (TrackEntry entry in _items.Keys.Where(e => e.State is TrackState.Queued or TrackState.Analyzing))
            {
                entry.State = TrackState.Pending;
                UpdateItem(entry);
            }
            if (_shown is not null)
            {
                ShowDetails(_shown);
            }
        }
        finally
        {
            _run = null;
            SetBusy(false);
            UpdateSummary(outcome);
        }

        if (_restartWhenIdle)
        {
            _restartWhenIdle = false;
            await RunAnalysisAsync();
        }
    }

    private void OnAnalysisProgress((TrackEntry Entry, TrackResult? Result) update)
    {
        (TrackEntry entry, TrackResult? result) = update;
        if (result is not null)
        {
            _runDone++;
            UpdateProgress();
        }
        if (!_items.TryGetValue(entry, out ListViewItem? item))
        {
            return; // removed from the list meanwhile
        }

        if (result is null)
        {
            if (entry.State == TrackState.Queued)
            {
                entry.State = TrackState.Analyzing;
            }
        }
        else
        {
            entry.State = TrackState.Done;
            entry.Result = result;
        }
        UpdateItem(entry);

        if (result is not null && fileList.SelectedItems.Count == 0)
        {
            // Show the first result straight away rather than an empty panel.
            item.Selected = true;
            item.Focused = true;
            item.EnsureVisible();
        }
        else if (ReferenceEquals(entry, _shown))
        {
            ShowDetails(entry);
        }
        UpdateSummary();
    }

    private void UpdateProgress()
    {
        progressBar.Maximum = Math.Max(1, _runTotal);
        progressBar.Value = Math.Min(_runDone, progressBar.Maximum);
    }

    private void SetBusy(bool busy)
    {
        cancelButton.Enabled = busy;
        analyzeButton.Enabled = !busy;
        ffmpegButton.Enabled = !busy;
        progressBar.Visible = busy;
    }

    // ── File list ────────────────────────────────────────────────────────────

    private void UpdateItem(TrackEntry entry)
    {
        if (!_items.TryGetValue(entry, out ListViewItem? item))
        {
            return;
        }
        TrackResult? result = entry.Result;
        AudioInfo? info = result?.Info;
        SpectralAnalysis? spectral = result?.Spectral;

        SetCell(item, TrackColumn.Format, info is null ? "" : Codecs.DisplayName(info.CodecName));
        SetCell(item, TrackColumn.SampleRate, info is { SampleRate: > 0 } ? $"{info.SampleRate / 1000.0:0.###} kHz" : "");
        SetCell(item, TrackColumn.Bits, info is { BitsPerSample: > 0 } ? $"{info.BitsPerSample}" : "");
        SetCell(item, TrackColumn.BitRate, info is { BitRate: > 0 } ? $"{info.BitRate / 1000.0:N0} kbps" : "");
        SetCell(item, TrackColumn.Content, spectral is null ? "" : $"{spectral.CutoffHz / 1000.0:0.00} kHz");
        SetCell(item, TrackColumn.Confidence, spectral is null ? "" : $"{spectral.CutoffConfidence:P0}");
        SetCell(item, TrackColumn.Source, spectral?.LikelySource ?? "");

        ListViewItem.ListViewSubItem status = item.SubItems[(int)TrackColumn.Status];
        if (result is null)
        {
            status.Text = entry.State == TrackState.Analyzing ? "Analyse…" : "En attente";
            status.BackColor = fileList.BackColor;
            status.ForeColor = SystemColors.GrayText;
        }
        else
        {
            (Color back, Color fore) = StatusStyle.For(result.Assessment);
            status.Text = result.Assessment.Label;
            status.BackColor = back;
            status.ForeColor = fore;
        }
    }

    private static void SetCell(ListViewItem item, TrackColumn column, string text) =>
        item.SubItems[(int)column].Text = text;

    private void fileList_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        var column = (TrackColumn)e.Column;
        SortOrder order =
            fileList.ListViewItemSorter is TrackComparer current
            && current.Column == column
            && current.Order == SortOrder.Ascending
                ? SortOrder.Descending
                : SortOrder.Ascending;
        // Assigning the comparer sorts the list.
        fileList.ListViewItemSorter = new TrackComparer(column, order);
    }

    private void fileList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Raised once per item whose selection changes: refresh once, after the last.
        if (_selectionRefreshPending)
        {
            return;
        }
        _selectionRefreshPending = true;
        BeginInvoke(() =>
        {
            _selectionRefreshPending = false;
            TrackEntry? entry = SelectedEntry();
            if (!ReferenceEquals(entry, _shown))
            {
                ShowDetails(entry);
            }
        });
    }

    private void fileList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.A)
        {
            fileList.BeginUpdate();
            foreach (ListViewItem item in fileList.Items)
            {
                item.Selected = true;
            }
            fileList.EndUpdate();
            e.Handled = true;
        }
    }

    private TrackEntry? SelectedEntry()
    {
        if (fileList.FocusedItem is { Selected: true } focused)
        {
            return (TrackEntry)focused.Tag!;
        }
        return fileList.SelectedItems.Count > 0 ? (TrackEntry)fileList.SelectedItems[0].Tag! : null;
    }

    private List<TrackEntry> SelectedEntries() =>
        fileList.SelectedItems.Cast<ListViewItem>().Select(item => (TrackEntry)item.Tag!).ToList();

    private void removeButton_Click(object? sender, EventArgs e) => RemoveSelected();

    private void RemoveSelected()
    {
        if (fileList.SelectedItems.Count == 0)
        {
            return;
        }
        fileList.BeginUpdate();
        try
        {
            foreach (ListViewItem item in fileList.SelectedItems.Cast<ListViewItem>().ToList())
            {
                var entry = (TrackEntry)item.Tag!;
                _items.Remove(entry);
                _knownPaths.Remove(entry.Path);
                fileList.Items.Remove(item);
            }
        }
        finally
        {
            fileList.EndUpdate();
        }
        UpdateSummary();
    }

    private void clearButton_Click(object? sender, EventArgs e)
    {
        _run?.Cancel();
        fileList.Items.Clear();
        _items.Clear();
        _knownPaths.Clear();
        ShowDetails(null);
        UpdateSummary();
    }

    private void fileMenu_Opening(object? sender, CancelEventArgs e)
    {
        int selected = fileList.SelectedItems.Count;
        if (selected == 0)
        {
            e.Cancel = true;
            return;
        }
        openLocationMenuItem.Enabled = selected == 1;
    }

    private void openLocationMenuItem_Click(object? sender, EventArgs e)
    {
        if (SelectedEntry() is not { } entry)
        {
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{entry.Path}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "Ouvrir l'emplacement", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async void reanalyzeMenuItem_Click(object? sender, EventArgs e)
    {
        // Entries queued or being analysed are left alone: their result is on its way.
        foreach (TrackEntry entry in SelectedEntries().Where(entry => entry.State is TrackState.Done or TrackState.Pending))
        {
            entry.State = TrackState.Pending;
            entry.Result = null;
            UpdateItem(entry);
        }
        if (_shown is not null)
        {
            ShowDetails(_shown);
        }
        UpdateSummary();
        await RunAnalysisAsync();
    }

    // ── Detail panel ─────────────────────────────────────────────────────────

    private void ShowDetails(TrackEntry? entry)
    {
        _shown = entry;
        TrackResult? result = entry?.Result;
        SpectralAnalysis? spectral = result?.Spectral;
        spectrogramView.Analysis = spectral;
        spectrumChart.Analysis = spectral;

        if (entry is null || result is null)
        {
            verdictLabel.ForeColor = SystemColors.GrayText;
            confidenceLabel.Text = "";
            confidenceBar.Visible = false;
            if (entry is null)
            {
                verdictLabel.Text = "Aucun fichier sélectionné";
                explanationLabel.Text =
                    "Ajoutez des fichiers audio (boutons ci-dessus ou glisser-déposer) : chacun est analysé pour savoir s'il est vraiment lossless.";
                detailsBox.Clear();
            }
            else
            {
                verdictLabel.Text = entry.State == TrackState.Analyzing ? "Analyse en cours…" : "En attente d'analyse";
                explanationLabel.Text = entry.FileName;
                WriteDetails(entry, null);
            }
            return;
        }

        verdictLabel.Text = result.Assessment.Label;
        verdictLabel.ForeColor = StatusStyle.For(result.Assessment).Fore;
        explanationLabel.Text = result.Assessment.Explanation;
        confidenceBar.Visible = spectral is not null;
        confidenceBar.Value = spectral is null ? 0 : (int)Math.Round(spectral.CutoffConfidence * 100);
        confidenceLabel.Text = spectral is null ? "" : $"Confiance de l'analyse spectrale : {spectral.CutoffConfidence:P0}";
        WriteDetails(entry, result);
    }

    private void WriteDetails(TrackEntry entry, TrackResult? result)
    {
        detailsBox.Clear();
        detailsBox.SelectionTabs = [LogicalToDeviceUnits(150)];

        AppendHeading("Fichier");
        AppendLine(entry.Path);
        if (result?.Info is { } info)
        {
            AppendLine(DescribeFormat(info));
        }
        if (result?.Error is { } error)
        {
            AppendHeading("Erreur");
            AppendLine(error);
        }
        if (result?.Spectral is { } s)
        {
            AppendHeading("Mesures spectrales");
            AppendField("Verdict spectral", DescribeVerdict(s));
            AppendField("Contenu jusqu'à", $"{s.CutoffHz / 1000.0:0.00} kHz (Nyquist : {s.NyquistHz / 1000.0:0.00} kHz)");
            AppendField("Pente la plus raide", s.CliffHz > 0 ? $"{s.ShelfDropDb:0.0} dB sur 1 kHz à {s.CliffHz / 1000.0:0.00} kHz" : "—");
            AppendField("Niveau au-dessus", $"{s.EnergyAboveCutoffDb:0} dB");
            AppendField("Extrait analysé", $"{s.TimeStartSec:0.0} s → {s.TimeEndSec:0.0} s ({s.FramesAnalyzed} trames)");

            AppendHeading("Preuves");
            foreach (string line in s.Evidence)
            {
                AppendLine("•  " + line);
            }
        }
        detailsBox.SelectionStart = 0;
        detailsBox.ScrollToCaret();
    }

    private static string DescribeFormat(AudioInfo info)
    {
        var parts = new List<string> { Codecs.DisplayName(info.CodecName) };
        if (info.SampleRate > 0)
        {
            parts.Add($"{info.SampleRate / 1000.0:0.###} kHz");
        }
        if (info.BitsPerSample > 0)
        {
            parts.Add($"{info.BitsPerSample} bits");
        }
        if (info.Channels > 0)
        {
            parts.Add(info.Channels switch { 1 => "mono", 2 => "stéréo", int n => $"{n} canaux" });
        }
        if (info.BitRate > 0)
        {
            parts.Add($"{info.BitRate / 1000.0:N0} kbps");
        }
        if (info.DurationSec > 0)
        {
            var duration = TimeSpan.FromSeconds(info.DurationSec);
            parts.Add(duration.ToString(duration.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"));
        }
        return string.Join(" · ", parts);
    }

    private static string DescribeVerdict(SpectralAnalysis s)
    {
        string verdict = s.Verdict switch
        {
            SpectralVerdict.Lossless => "cohérent avec du lossless",
            SpectralVerdict.Lossy => $"signature d'un encodeur avec perte ({s.LikelySource})",
            _ => "non concluant",
        };
        return $"{verdict}, confiance {s.CutoffConfidence:P0}";
    }

    private void AppendHeading(string text)
    {
        if (detailsBox.TextLength > 0)
        {
            Append("\n");
        }
        Append(text + "\n", _headingFont);
    }

    private void AppendLine(string text) => Append(text + "\n");

    private void AppendField(string label, string value)
    {
        Append(label + "\t", color: SystemColors.GrayText);
        Append(value + "\n");
    }

    private void Append(string text, Font? font = null, Color? color = null)
    {
        detailsBox.SelectionStart = detailsBox.TextLength;
        detailsBox.SelectionLength = 0;
        detailsBox.SelectionFont = font ?? detailsBox.Font;
        detailsBox.SelectionColor = color ?? detailsBox.ForeColor;
        detailsBox.AppendText(text);
    }

    private void UpdateSummary(string? prefix = null)
    {
        int total = _items.Count;
        if (total == 0)
        {
            summaryLabel.Text = prefix ?? "Glissez-déposez des fichiers ou des dossiers audio pour commencer.";
            return;
        }

        var parts = new List<string>();
        if (prefix is not null)
        {
            parts.Add(prefix);
        }
        parts.Add(Count(total, "fichier", "fichiers"));
        int done = _items.Keys.Count(entry => entry.Result is not null);
        if (done < total)
        {
            parts.Add(Count(done, "analysé", "analysés"));
        }

        Dictionary<LosslessStatus, int> byStatus = _items.Keys
            .Where(entry => entry.Result is not null)
            .GroupBy(entry => entry.Result!.Assessment.Status)
            .ToDictionary(group => group.Key, group => group.Count());
        AddCount(LosslessStatus.Authentic, "lossless authentique", "lossless authentiques");
        AddCount(LosslessStatus.FakeLossless, "faux lossless", "faux lossless");
        AddCount(LosslessStatus.Suspect, "suspect", "suspects");
        AddCount(LosslessStatus.Inconclusive, "non concluant", "non concluants");
        AddCount(LosslessStatus.LossyFormat, "format avec perte", "formats avec perte");
        AddCount(LosslessStatus.Error, "erreur", "erreurs");
        summaryLabel.Text = string.Join(" · ", parts);

        void AddCount(LosslessStatus status, string singular, string plural)
        {
            if (byStatus.TryGetValue(status, out int n))
            {
                parts.Add(Count(n, singular, plural));
            }
        }
    }

    // French: 0 and 1 take the singular.
    private static string Count(int n, string singular, string plural) => $"{n} {(n > 1 ? plural : singular)}";

    // ── Settings ─────────────────────────────────────────────────────────────

    private void paletteComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var palette = (SpectrogramPalette)Math.Max(0, paletteComboBox.SelectedIndex);
        spectrogramView.Palette = palette;
        if (_settings.Palette != palette)
        {
            _settings.Palette = palette;
            _settings.Save();
        }
    }

    private async void ffmpegButton_Click(object? sender, EventArgs e) => await ChooseFfmpegAsync();

    private async void ffmpegLabel_Click(object? sender, EventArgs e)
    {
        if (_run is null)
        {
            await ChooseFfmpegAsync();
        }
    }

    private async Task AskForFfmpegAsync()
    {
        DialogResult answer = MessageBox.Show(
            this,
            "LosslessOrNot utilise ffmpeg et ffprobe pour décoder l'audio, mais ils sont introuvables "
            + "(ni dans le dossier de l'application, ni dans le PATH).\n\nVoulez-vous indiquer l'emplacement de ffmpeg.exe ?",
            "ffmpeg introuvable",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (answer == DialogResult.Yes)
        {
            await ChooseFfmpegAsync();
        }
    }

    private async Task ChooseFfmpegAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Indiquer l'emplacement de ffmpeg.exe",
            Filter = "ffmpeg|ffmpeg.exe|Programmes (*.exe)|*.exe",
            FileName = "ffmpeg.exe",
        };
        if (_tools is not null)
        {
            dialog.InitialDirectory = _tools.Folder;
        }
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        FfmpegTools? tools = FfmpegLocator.Locate(dialog.FileName);
        if (tools is null)
        {
            MessageBox.Show(
                this,
                "ffprobe.exe est introuvable : il doit se trouver dans le même dossier que ffmpeg.exe.",
                "ffmpeg",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }
        _tools = tools;
        _settings.FfmpegPath = dialog.FileName;
        _settings.Save();
        UpdateFfmpegStatus();
        await RunAnalysisAsync();
    }

    private void UpdateFfmpegStatus()
    {
        if (_tools is null)
        {
            ffmpegLabel.Text = "ffmpeg introuvable — cliquez ici pour l'indiquer";
            ffmpegLabel.ForeColor = Color.Firebrick;
            ffmpegLabel.ToolTipText = "LosslessOrNot utilise ffmpeg et ffprobe pour décoder l'audio.";
        }
        else
        {
            ffmpegLabel.Text = $"ffmpeg : {_tools.Folder}";
            ffmpegLabel.ForeColor = SystemColors.ControlText;
            ffmpegLabel.ToolTipText = $"{_tools.FfmpegPath}\n{_tools.FfprobePath}\nCliquez pour en choisir un autre.";
        }
    }
}
