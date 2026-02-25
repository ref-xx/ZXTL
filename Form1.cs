using System.Text;

namespace ZXTL
{
    public partial class Form1 : Form
    {
        private readonly LogPaneState _primaryPane = new();
        private readonly LogPaneState _secondaryPane = new();
        private TraceLogData PrimaryLog => _primaryPane.LogData;
        private TraceLogData SecondaryLog => _secondaryPane.LogData;

        public Form1()
        {
            InitializeComponent();

            _primaryPane.Initialize(groupBoxPrimary, listBox1, "Primary Log");
            _secondaryPane.Initialize(groupBoxSecondary, listBox2, "Secondary Log");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _primaryPane.Document?.Dispose();
            _secondaryPane.Document?.Dispose();
            base.OnFormClosed(e);
        }

        private async void openPrimaryLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await OpenTraceLogAsync(_primaryPane, "Open Primary Trace Log");
        }

        private async void openSecondaryLogToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await OpenTraceLogAsync(_secondaryPane, "Open Secondary Trace Log");
        }

        private async Task OpenTraceLogAsync(LogPaneState pane, string dialogTitle)
        {
            using OpenFileDialog dialog = new()
            {
                Title = dialogTitle,
                Multiselect = false,
                Filter = "Trace Logs (*.log)|*.log|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            await LoadTraceLogAsync(pane, dialog.FileName);
        }

        private async Task LoadTraceLogAsync(LogPaneState pane, string filePath)
        {
            pane.LoadVersion++;
            int loadVersion = pane.LoadVersion;

            pane.Document?.Dispose();
            pane.Document = null;
            pane.VisibleStartLine = 0;
            pane.LogData.Reset();

            TraceLogDocument document = new(filePath);
            pane.Document = document;
            pane.GroupBox.Text = $"{pane.BaseTitle} - {Path.GetFileName(filePath)}";

            SetListBoxItems(
                pane.ListBox,
                new[]
                {
                    $"Loading preview: {Path.GetFileName(filePath)}",
                    $"Size: {FormatFileSize(document.FileSizeBytes)}"
                });

            try
            {
                int previewCount = CalculatePreviewLineCount(pane.ListBox);
                IReadOnlyList<string> previewLines = await document.ReadInitialPreviewAsync(previewCount, document.Token);

                if (pane.LoadVersion != loadVersion || !ReferenceEquals(pane.Document, document))
                {
                    return;
                }

                if (previewLines.Count > 0)
                {
                    TryPopulateTemplateData(pane.LogData, previewLines[0]);
                }

                ReportLogDetails(pane.LogData);
                SetListBoxItems(pane.ListBox, previewLines);
                document.StartBackgroundIndexing();
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(pane.Document, document))
                {
                    pane.Document = null;
                    pane.LogData.Reset();
                    pane.GroupBox.Text = pane.BaseTitle;
                    SetListBoxItems(pane.ListBox, new[] { "Load canceled." });
                }
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(pane.Document, document))
                {
                    pane.Document = null;
                    pane.LogData.Reset();
                    pane.GroupBox.Text = pane.BaseTitle;
                    SetListBoxItems(pane.ListBox, new[] { "Failed to open log." });
                }

                document.Dispose();
                MessageBox.Show(
                    this,
                    ex.Message,
                    "Open Trace Log",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static int CalculatePreviewLineCount(ListBox listBox)
        {
            int itemHeight = Math.Max(1, listBox.ItemHeight);
            int visibleLineCount = (int)Math.Ceiling(listBox.ClientSize.Height / (double)itemHeight);
            return Math.Max(1, visibleLineCount + 1);
        }

        private static void SetListBoxItems(ListBox listBox, IReadOnlyList<string> lines)
        {
            listBox.BeginUpdate();
            try
            {
                listBox.Items.Clear();

                if (lines.Count == 0)
                {
                    listBox.Items.Add("(empty file)");
                    return;
                }

                foreach (string line in lines)
                {
                    listBox.Items.Add(line);
                }
            }
            finally
            {
                listBox.EndUpdate();
            }
        }

        private static string FormatFileSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double value = bytes;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }

        private static void TryPopulateTemplateData(TraceLogData logData, string firstLine)
        {
            logData.Reset();

            if (!TrySplitCsv(firstLine, out IReadOnlyList<string> fields) || fields.Count != 4)
            {
                return;
            }

            if (!StartsWithZxtlWord(fields[0]))
            {
                return;
            }

            TraceLogTemplateHeader header = new()
            {
                SignatureAndVersion = fields[0],
                EmulatorName = fields[1],
                Order = fields[2],
                Options = fields[3]
            };

            logData.SetTemplateHeader(header);
        }

        private static bool StartsWithZxtlWord(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            ReadOnlySpan<char> span = value.AsSpan().TrimStart();

            while (!span.IsEmpty && (span[0] == '\uFEFF' || span[0] == '"' || span[0] == '\''))
            {
                span = span[1..];
            }

            int length = 0;

            while (length < span.Length && !char.IsWhiteSpace(span[length]) && span[length] != ',')
            {
                length++;
            }

            return length == 4 && span[..length].Equals("ZXTL".AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TrySplitCsv(string line, out IReadOnlyList<string> fields)
        {
            List<string> result = new(4);
            StringBuilder current = new();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                        continue;
                    }

                    inQuotes = !inQuotes;
                    continue;
                }

                if (!inQuotes && c == ',')
                {
                    result.Add(current.ToString().Trim());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (inQuotes)
            {
                fields = Array.Empty<string>();
                return false;
            }

            result.Add(current.ToString().Trim());
            fields = result;
            return true;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ReportLogDetails(TraceLogData logData)
        {
            StringBuilder sb = new();

            string sourceName = ReferenceEquals(logData, PrimaryLog)
                ? "PrimaryLog"
                : ReferenceEquals(logData, SecondaryLog)
                    ? "SecondaryLog"
                    : "TraceLog";

            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {sourceName}");

            if (!logData.HasTemplateHeader || logData.TemplateHeader is null)
            {
                sb.AppendLine("Template: none (first line does not start with ZXTL)");
            }
            else
            {
                TraceLogTemplateHeader header = logData.TemplateHeader;

                sb.AppendLine("Template: ZXTL");
                sb.AppendLine($"Version: {header.SignatureAndVersion}");
                sb.AppendLine($"Emulator: {header.EmulatorName}");
                sb.AppendLine($"Order: {header.Order}");
                sb.AppendLine($"Options: {header.Options}");
                sb.AppendLine($"Opts.Model: {logData.Opts.Model}");
                sb.AppendLine($"Opts.Slice: {logData.Opts.Slice}");
                sb.AppendLine($"Opts.ViewMem: {logData.Opts.ViewMem}");
                sb.AppendLine($"Opts.Hex: {logData.Opts.Hex}");
                sb.AppendLine($"Opts.HexPrefix: {(string.IsNullOrEmpty(logData.Opts.HexPrefix) ? "None" : logData.Opts.HexPrefix)}");
                sb.AppendLine($"Opts.SnapshotFile: {logData.Opts.SnapshotFile ?? "(null)"}");
            }

            sb.AppendLine();
            txtLog.AppendText(sb.ToString());
        }

        private sealed class TraceLogData
        {
            public TraceLogTemplateHeader? TemplateHeader { get; private set; }
            public TraceLogOptions Opts { get; private set; } = new();
            public TraceLogOrderDefinition OrderDefinition { get; } = new();
            public TraceLogLineData SelectedLine { get; } = new();
            public bool HasTemplateHeader => TemplateHeader is not null;

            public void Reset()
            {
                TemplateHeader = null;
                Opts = new TraceLogOptions();
                OrderDefinition.Clear();
                SelectedLine.Clear();
            }

            public void SetTemplateHeader(TraceLogTemplateHeader header)
            {
                TemplateHeader = header;
                Opts = TraceLogOptionsParser.ParseDefineLine(header.Options);
                OrderDefinition.RawText = header.Order;
            }
        }

        private sealed class TraceLogTemplateHeader
        {
            public string SignatureAndVersion { get; init; } = string.Empty;
            public string EmulatorName { get; init; } = string.Empty;
            public string Order { get; init; } = string.Empty;
            public string Options { get; init; } = string.Empty;
        }

        private sealed class LogPaneState
        {
            public GroupBox GroupBox { get; private set; } = null!;
            public ListBox ListBox { get; private set; } = null!;
            public string BaseTitle { get; private set; } = string.Empty;
            public TraceLogDocument? Document { get; set; }
            public TraceLogData LogData { get; } = new();
            public int VisibleStartLine { get; set; }
            public int LoadVersion { get; set; }

            public void Initialize(GroupBox groupBox, ListBox listBox, string baseTitle)
            {
                GroupBox = groupBox;
                ListBox = listBox;
                BaseTitle = baseTitle;
            }
        }

        private sealed class TraceLogDocument : IDisposable
        {
            private readonly CancellationTokenSource _lifetimeCts = new();
            private readonly object _indexLock = new();
            private Task? _indexTask;
            private List<long>? _lineStartOffsets;

            public TraceLogDocument(string filePath)
            {
                FilePath = filePath;
                FileSizeBytes = new FileInfo(filePath).Length;
            }

            public string FilePath { get; }
            public long FileSizeBytes { get; }
            public CancellationToken Token => _lifetimeCts.Token;
            public bool IsLineIndexComplete { get; private set; }

            public int IndexedLineCount
            {
                get
                {
                    lock (_indexLock)
                    {
                        return _lineStartOffsets?.Count ?? 0;
                    }
                }
            }

            public Task<IReadOnlyList<string>> ReadInitialPreviewAsync(int lineCount, CancellationToken cancellationToken)
            {
                return Task.Run<IReadOnlyList<string>>(() =>
                {
                    List<string> lines = new(Math.Max(1, lineCount));

                    using FileStream stream = new(
                        FilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        1024 * 1024,
                        FileOptions.SequentialScan);

                    using StreamReader reader = new(
                        stream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true,
                        bufferSize: 1024 * 1024);

                    while (lines.Count < lineCount && !reader.EndOfStream)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        lines.Add(reader.ReadLine() ?? string.Empty);
                    }

                    return (IReadOnlyList<string>)lines;
                }, cancellationToken);
            }

            public void StartBackgroundIndexing()
            {
                _indexTask ??= Task.Run(() => BuildLineIndex(_lifetimeCts.Token), _lifetimeCts.Token);
            }

            private void BuildLineIndex(CancellationToken cancellationToken)
            {
                List<long> offsets = [0];

                using FileStream stream = new(
                    FilePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    1024 * 1024,
                    FileOptions.SequentialScan);

                byte[] buffer = new byte[1024 * 1024];
                long absoluteOffset = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    for (int i = 0; i < bytesRead; i++)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            offsets.Add(absoluteOffset + i + 1);
                        }
                    }

                    absoluteOffset += bytesRead;
                }

                if (offsets.Count > 0 && offsets[^1] == absoluteOffset)
                {
                    offsets.RemoveAt(offsets.Count - 1);
                }

                lock (_indexLock)
                {
                    _lineStartOffsets = offsets;
                    IsLineIndexComplete = true;
                }
            }

            public void Dispose()
            {
                if (_lifetimeCts.IsCancellationRequested)
                {
                    return;
                }

                _lifetimeCts.Cancel();
                _lifetimeCts.Dispose();
            }
        }
    }
}
