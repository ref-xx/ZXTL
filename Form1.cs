using System.Text;
using System.Globalization;

namespace ZXTL
{
    public partial class Form1 : Form
    {
        private readonly LogPaneState _primaryPane = new();
        private readonly LogPaneState _secondaryPane = new();
        private readonly Color _registerDefaultBackColor;
        private readonly Color _registerDefaultForeColor;
        private readonly Color _registerChangedBackColor = SystemColors.Info;
        private readonly Color _registerDiffForeColor = Color.Red;
        private readonly List<TemplateAvailableFieldEntry> _templateAvailableFields = new();
        private TraceLogData PrimaryLog => _primaryPane.LogData;
        private TraceLogData SecondaryLog => _secondaryPane.LogData;

        public Form1()
        {
            InitializeComponent();
            _registerDefaultBackColor = txtPC1.BackColor;
            _registerDefaultForeColor = txtPC1.ForeColor;

            _primaryPane.Initialize(groupBoxPrimary, listBox1, "Primary Log");
            _secondaryPane.Initialize(groupBoxSecondary, listBox2, "Secondary Log");
            WireOptionalHeaderVisibilityCheckboxes();
            WirePaneSelectionHandlers();
            WireNavigationButtons();
            WireTemplateEditorButtons();
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
            pane.PreviewLines.Clear();
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

                pane.PreviewLines.Clear();
                pane.PreviewLines.AddRange(previewLines);

                if (pane.LogData.HasTemplateHeader)
                {
                    pane.LogData.HeaderLine = previewLines.Count > 1 ? previewLines[1] : null;
                }

                ReportLogDetails(pane.LogData);
                await RefreshPaneViewportAsync(pane);
                document.StartBackgroundIndexing();
            }
            catch (OperationCanceledException)
            {
                if (ReferenceEquals(pane.Document, document))
                {
                    pane.Document = null;
                    pane.PreviewLines.Clear();
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
                    pane.PreviewLines.Clear();
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

        private void WireOptionalHeaderVisibilityCheckboxes()
        {
            TryWireHeaderVisibilityCheckbox("chkPrimaryHeader", _primaryPane);
            TryWireHeaderVisibilityCheckbox("chkSecondaryHeader", _secondaryPane);
        }

        private void WirePaneSelectionHandlers()
        {
            _primaryPane.ListBox.SelectedIndexChanged += (_, _) => OnPreviewSelectionChanged(_primaryPane);
            _secondaryPane.ListBox.SelectedIndexChanged += (_, _) => OnPreviewSelectionChanged(_secondaryPane);
        }

        private void TryWireHeaderVisibilityCheckbox(string checkboxName, LogPaneState pane)
        {
            if (FindControlRecursive<CheckBox>(this, checkboxName) is not CheckBox checkBox)
            {
                return;
            }

            pane.HeaderVisibilityCheckBox = checkBox;
            checkBox.CheckedChanged += async (_, _) => await RefreshPaneViewportAsync(pane);
        }

        private void WireNavigationButtons()
        {
            btnNextLine.Click += async (_, _) => await ScrollPaneAsync(_primaryPane, +1);
            btnPrevLine.Click += async (_, _) => await ScrollPaneAsync(_primaryPane, -1);
            btnNextLine2.Click += async (_, _) => await ScrollPaneAsync(_secondaryPane, +1);
            btnPrevline2.Click += async (_, _) => await ScrollPaneAsync(_secondaryPane, -1);

            btnNextBoth.Click += async (_, _) => await ScrollBothPanesAsync(+1);
            btnPrevBoth.Click += async (_, _) => await ScrollBothPanesAsync(-1);
        }

        private void WireTemplateEditorButtons()
        {
            btnGrabLine.Click += btnGrabLine_Click;
            richTextBox1.SelectionChanged += richTextBox1_SelectionChanged;
            listSingleRegs.DoubleClick += TemplateFieldSource_DoubleClick;
            listPairs.DoubleClick += TemplateFieldSource_DoubleClick;
            listKeywords.DoubleClick += TemplateFieldSource_DoubleClick;
            btnFieldUp.Click += btnFieldUp_Click;
            btnFieldDown.Click += btnFieldDown_Click;
            btnFieldRemove.Click += btnFieldRemove_Click;
            btnFieldApply.Click += btnFieldApply_Click;
        }

        private void btnGrabLine_Click(object? sender, EventArgs e)
        {
            PopulateAvailableFieldsFromPrimaryZxtl();

            if (listBox1.SelectedItem is not string selectedLine)
            {
                MessageBox.Show(
                    this,
                    "Load a trace log to the Primary Buffer first.",
                    "Grab Trace Line",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if ((richTextBox1.TextLength > 0)&&(richTextBox1.Text != "Press 'Grab Trace' to Start"))
            {
                DialogResult overwriteResult = MessageBox.Show(
                    this,
                    "This will clear any trace edits. Are you sure?",
                    "Grab Trace Line",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (overwriteResult != DialogResult.Yes)
                {
                    return;
                }
            }

            richTextBox1.Text = selectedLine;
            richTemplate.Text = "ZXTL V" + (comboVersions.SelectedItem?.ToString() ?? "0003" ) + "," + txtEmulatorName.Text + ",";
            txtTracePreview.Text = "";
        }

        private void PopulateAvailableFieldsFromPrimaryZxtl()
        {
            TraceLogData primaryLog = _primaryPane.LogData;

            if (!primaryLog.HasTemplateHeader || primaryLog.TemplateHeader is null)
            {
                _templateAvailableFields.Clear();
                RefreshAvailableFieldsListDisplay(-1);
                return;
            }

            _templateAvailableFields.Clear();
            IReadOnlyList<TraceLogOrderFieldSpec> items = primaryLog.OrderDefinition.Items;
            for (int i = 0; i < items.Count; i++)
            {
                _templateAvailableFields.Add(new TemplateAvailableFieldEntry(i, items[i]));
            }

            RefreshAvailableFieldsListDisplay(_templateAvailableFields.Count > 0 ? 0 : -1);

            tabOptions.SelectedTab = tabPage3;
        }

        private void RefreshAvailableFieldsListDisplay(int selectedIndex)
        {
            listAvailableFields.BeginUpdate();
            try
            {
                listAvailableFields.Items.Clear();

                for (int i = 0; i < _templateAvailableFields.Count; i++)
                {
                    TemplateAvailableFieldEntry entry = _templateAvailableFields[i];
                    string label = $"{entry.SourceIndex:00}: {TraceLogOrderParser.Describe(entry.Item)}";
                    listAvailableFields.Items.Add(label);
                }

                if (selectedIndex >= 0 && selectedIndex < listAvailableFields.Items.Count)
                {
                    listAvailableFields.SelectedIndex = selectedIndex;
                }
            }
            finally
            {
                listAvailableFields.EndUpdate();
            }
        }

        private void btnFieldUp_Click(object? sender, EventArgs e)
        {
            if (!TryGetAvailableFieldSelection(out int selectedIndex))
            {
                ShowTemplateEditorError("Select a field in Available Fields first.");
                return;
            }

            if (selectedIndex <= 0)
            {
                ApplyTracePreviewFromAvailableFields();
                return;
            }

            (_templateAvailableFields[selectedIndex - 1], _templateAvailableFields[selectedIndex]) =
                (_templateAvailableFields[selectedIndex], _templateAvailableFields[selectedIndex - 1]);

            RefreshAvailableFieldsListDisplay(selectedIndex - 1);
            ApplyTracePreviewFromAvailableFields();
        }

        private void btnFieldDown_Click(object? sender, EventArgs e)
        {
            if (!TryGetAvailableFieldSelection(out int selectedIndex))
            {
                ShowTemplateEditorError("Select a field in Available Fields first.");
                return;
            }

            if (selectedIndex >= _templateAvailableFields.Count - 1)
            {
                ApplyTracePreviewFromAvailableFields();
                return;
            }

            (_templateAvailableFields[selectedIndex], _templateAvailableFields[selectedIndex + 1]) =
                (_templateAvailableFields[selectedIndex + 1], _templateAvailableFields[selectedIndex]);

            RefreshAvailableFieldsListDisplay(selectedIndex + 1);
            ApplyTracePreviewFromAvailableFields();
        }

        private void btnFieldRemove_Click(object? sender, EventArgs e)
        {
            if (!TryGetAvailableFieldSelection(out int selectedIndex))
            {
                ShowTemplateEditorError("Select a field in Available Fields first.");
                return;
            }

            _templateAvailableFields.RemoveAt(selectedIndex);
            int nextSelection = _templateAvailableFields.Count == 0
                ? -1
                : Math.Min(selectedIndex, _templateAvailableFields.Count - 1);

            RefreshAvailableFieldsListDisplay(nextSelection);
            ApplyTracePreviewFromAvailableFields();
        }

        private void btnFieldApply_Click(object? sender, EventArgs e)
        {
            if (_templateAvailableFields.Count == 0)
            {
                ShowTemplateEditorError("There are no available fields to build the template ORDER section.");
                return;
            }

            richTemplate.Text = BuildFullTemplateFromEditorState();
        }

        private bool TryGetAvailableFieldSelection(out int selectedIndex)
        {
            selectedIndex = listAvailableFields.SelectedIndex;
            return selectedIndex >= 0 &&
                selectedIndex < _templateAvailableFields.Count;
        }

        private void ApplyTracePreviewFromAvailableFields()
        {
            if (_templateAvailableFields.Count == 0)
            {
                txtTracePreview.Text = string.Empty;
                return;
            }

            if (!PrimaryLog.HasTemplateHeader || PrimaryLog.TemplateHeader is null)
            {
                ShowTemplateEditorError("Primary log is not a valid ZXTL trace log.");
                return;
            }

            if (!TryGetTemplateEditorSourceTraceLine(out string sourceLine))
            {
                ShowTemplateEditorError("Grab a trace line first.");
                return;
            }

            if (TryParseTracePreviewLineByOrder(
                sourceLine,
                PrimaryLog.OrderDefinition.Items,
                PrimaryLog.Opts.IsTabbed,
                out List<string> values,
                out string? parseError))
            {
                txtTracePreview.Text = BuildReorderedTracePreviewText(values, PrimaryLog.Opts.IsTabbed);
                return;
            }

            ShowTemplateEditorError($"Preview build failed: {parseError}");
        }

        private bool TryGetTemplateEditorSourceTraceLine(out string line)
        {
            line = richTextBox1.Text;

            if (!string.IsNullOrWhiteSpace(line) &&
                !string.Equals(line, "Press 'Grab Trace' to Start", StringComparison.Ordinal))
            {
                return true;
            }

            if (listBox1.SelectedItem is string selected && !string.IsNullOrWhiteSpace(selected))
            {
                if (IsHeaderDisplaySelection(_primaryPane, listBox1.SelectedIndex))
                {
                    line = string.Empty;
                    return false;
                }

                line = selected;
                return true;
            }

            line = string.Empty;
            return false;
        }

        private string BuildReorderedTracePreviewText(IReadOnlyList<string> parsedValues, bool isTabbed)
        {
            string separator = isTabbed ? "\t" : " ";
            StringBuilder sb = new();

            for (int i = 0; i < _templateAvailableFields.Count; i++)
            {
                TemplateAvailableFieldEntry entry = _templateAvailableFields[i];
                if (entry.SourceIndex < 0 || entry.SourceIndex >= parsedValues.Count)
                {
                    continue;
                }

                if (i > 0)
                {
                    sb.Append(separator);
                }

                sb.Append(FormatPreviewFieldValue(entry.Item, parsedValues[entry.SourceIndex]));
            }

            return sb.ToString();
        }

        private static string FormatPreviewFieldValue(TraceLogOrderFieldSpec item, string value)
        {
            string text = value ?? string.Empty;

            if (item.FixedWidth is int width && width > 0)
            {
                if (text.Length > width)
                {
                    return text[..width];
                }

                return text.PadRight(width);
            }

            return text;
        }

        private string BuildFullTemplateFromEditorState()
        {
            string version = comboVersions.SelectedItem?.ToString() ?? "0003";
            string emulatorName = (txtEmulatorName.Text ?? string.Empty).Trim();
            string orderSection = BuildOrderSectionFromAvailableFields();
            string optionsSection = BuildOptionsSectionFromEditorState();

            return $"ZXTL V{version}, {emulatorName}, {orderSection}, {optionsSection}";
        }

        private string BuildOrderSectionFromAvailableFields()
        {
            if (_templateAvailableFields.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                _templateAvailableFields.Select(static entry => entry.Item.RawToken.Trim()));
        }

        private string BuildOptionsSectionFromEditorState()
        {
            List<string> tokens = new();

            if (chkUseViewmem.Checked)
            {
                tokens.Add("VIEWMEM");
            }

            if (chkUseTabbed.Checked)
            {
                tokens.Add("TABBED");
            }

            if (chkUseJumps.Checked)
            {
                tokens.Add("ONLYJUMPS");
            }

            if (chkUseSlice.Checked)
            {
                tokens.Add("SLICE");
            }

            if (chkUseHex.Checked)
            {
                tokens.Add("HEX");
            }

            if (chkUseHexPrefix.Checked)
            {
                string prefix = comboPrefixes.Text?.Trim() ?? string.Empty;
                if (prefix.Length > 0)
                {
                    tokens.Add($"PREFIXED={prefix}");
                }
            }

            string model = comboModel.SelectedItem?.ToString() ?? comboModel.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(model))
            {
                tokens.Add($"M={model.Trim()}");
            }

            string snapshotFile = txtSnapshotFile.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(snapshotFile) &&
                !string.Equals(snapshotFile, "Not Set.", StringComparison.OrdinalIgnoreCase))
            {
                tokens.Add($"S={QuoteOptionValueIfNeeded(snapshotFile)}");
            }

            return string.Join(" ", tokens);
        }

        private static string QuoteOptionValueIfNeeded(string value)
        {
            if (value.IndexOfAny([' ', '\t', ',']) >= 0)
            {
                return $"\"{value.Replace("\"", "")}\"";
            }

            return value;
        }

        private void richTextBox1_SelectionChanged(object? sender, EventArgs e)
        {
            if (!chkAutoFieldLen.Checked)
            {
                return;
            }

            txtFieldLen.Text = richTextBox1.SelectionLength.ToString(CultureInfo.InvariantCulture);
        }

        private void TemplateFieldSource_DoubleClick(object? sender, EventArgs e)
        {
            if (sender is not ListBox sourceList)
            {
                return;
            }

            if (sourceList.SelectedIndex < 0 || sourceList.SelectedItem is null)
            {
                ShowTemplateEditorError("Double-click an item in the list.");
                return;
            }

            int selectionStart = richTextBox1.SelectionStart;
            int selectionLength = richTextBox1.SelectionLength;

            if (selectionLength <= 0)
            {
                ShowTemplateEditorError("Select a region in the trace line before double-clicking a template field.");
                return;
            }

            if (SelectionContainsNonDefaultFormatting(richTextBox1, selectionStart, selectionLength))
            {
                ShowTemplateEditorError("The selected region already contains colored text. Select an uncolored region.");
                return;
            }

            string rawItemText = sourceList.SelectedItem.ToString() ?? string.Empty;
            string itemText = rawItemText.Trim();
            string token;

            if (ReferenceEquals(sourceList, listPairs) || ReferenceEquals(sourceList, listSingleRegs))
            {
                token = "R" + itemText;
            }
            else if (ReferenceEquals(sourceList, listKeywords))
            {
                token = itemText;
            }
            else
            {
                ShowTemplateEditorError("Unsupported template source list.");
                return;
            }

            if (ChkFieldLen.Checked)
            {
                if (!int.TryParse(txtFieldLen.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int fieldLength) ||
                    fieldLength <= 0)
                {
                    ShowTemplateEditorError("Field length must be a number greater than 0.");
                    return;
                }

                token += $"#{fieldLength}";
            }

            Color tokenColor = MakeColor(sourceList.Tag, sourceList.SelectedIndex);
            ApplyColorToSelection(richTextBox1, selectionStart, selectionLength, tokenColor);
            AppendColoredTokenToTemplate(token, tokenColor);
        }

        private void ShowTemplateEditorError(string message)
        {
            MessageBox.Show(
                this,
                message,
                "Template Editor",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private static bool SelectionContainsNonDefaultFormatting(RichTextBox box, int start, int length)
        {
            if (length <= 0)
            {
                return false;
            }

            int originalStart = box.SelectionStart;
            int originalLength = box.SelectionLength;

            try
            {
                for (int i = 0; i < length; i++)
                {
                    box.Select(start + i, 1);

                    Color fore = box.SelectionColor;
                    Color back = box.SelectionBackColor;

                    bool isDefaultFore = fore.IsEmpty || fore.ToArgb() == box.ForeColor.ToArgb();
                    bool isDefaultBack = back.IsEmpty || back.ToArgb() == box.BackColor.ToArgb();

                    if (!isDefaultFore || !isDefaultBack)
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                box.Select(originalStart, originalLength);
            }
        }

        private static void ApplyColorToSelection(RichTextBox box, int start, int length, Color color)
        {
            int originalStart = box.SelectionStart;
            int originalLength = box.SelectionLength;

            try
            {
                box.Select(start, length);
                box.SelectionBackColor = color;
                box.SelectionColor = box.ForeColor;
            }
            finally
            {
                box.Select(originalStart, originalLength);
            }
        }

        private void AppendColoredTokenToTemplate(string token, Color color)
        {
            int originalStart = richTemplate.SelectionStart;
            int originalLength = richTemplate.SelectionLength;

            try
            {
                richTemplate.Select(richTemplate.TextLength, 0);
                richTemplate.SelectionBackColor = color;
                richTemplate.SelectionColor = richTemplate.ForeColor;
                richTemplate.SelectedText = token;
                richTemplate.SelectionBackColor = richTemplate.BackColor;
                richTemplate.SelectionColor = richTemplate.ForeColor;
            }
            finally
            {
                richTemplate.Select(originalStart, originalLength);
            }
        }

        private static Color MakeColor(object? tagValue, int index)
        {
            int tag = 0;
            if (tagValue is not null)
            {
                _ = int.TryParse(
                    Convert.ToString(tagValue, CultureInfo.InvariantCulture),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out tag);
            }

            unchecked
            {
                uint hash = 2166136261;
                hash = (hash ^ (uint)tag) * 16777619;
                hash = (hash ^ (uint)Math.Max(0, index)) * 16777619;
                hash ^= hash >> 13;
                hash *= 1274126177;
                hash ^= hash >> 16;

                int r = 96 + (int)(hash & 0x5F);
                int g = 96 + (int)((hash >> 8) & 0x5F);
                int b = 96 + (int)((hash >> 16) & 0x5F);
                return Color.FromArgb(r, g, b);
            }
        }

        private async Task ScrollBothPanesAsync(int delta)
        {
            await Task.WhenAll(
                ScrollPaneAsync(_primaryPane, delta),
                ScrollPaneAsync(_secondaryPane, delta));
        }

        private async Task ScrollPaneAsync(LogPaneState pane, int delta)
        {
            if (pane.Document is null)
            {
                return;
            }

            pane.VisibleStartLine += delta;
            await RefreshPaneViewportAsync(pane);
        }

        private async Task RefreshPaneViewportAsync(LogPaneState pane)
        {
            TraceLogDocument? document = pane.Document;

            if (document is null)
            {
                return;
            }

            int loadVersion = pane.LoadVersion;
            int totalDisplayCapacity = CalculatePreviewLineCount(pane.ListBox);

            bool isZxtl = pane.LogData.HasTemplateHeader;
            bool showHeader = isZxtl &&
                !string.IsNullOrEmpty(pane.LogData.HeaderLine) &&
                pane.HeaderVisibilityCheckBox?.Checked != true;

            int fixedHeaderCount = showHeader ? 1 : 0;
            int traceCapacity = Math.Max(1, totalDisplayCapacity - fixedHeaderCount);
            int dataStartAbsoluteLine = isZxtl ? 2 : 0;

            IReadOnlyList<string> traceLines;
            int totalTraceLinesKnown;

            if (document.IsLineIndexComplete)
            {
                int totalLines = document.IndexedLineCount;
                totalTraceLinesKnown = Math.Max(0, totalLines - dataStartAbsoluteLine);
                pane.VisibleStartLine = ClampVisibleStart(pane.VisibleStartLine, totalTraceLinesKnown, traceCapacity);

                traceLines = await document.ReadLinesAsync(
                    dataStartAbsoluteLine + pane.VisibleStartLine,
                    traceCapacity,
                    document.Token);
            }
            else
            {
                totalTraceLinesKnown = Math.Max(0, pane.PreviewLines.Count - dataStartAbsoluteLine);
                pane.VisibleStartLine = ClampVisibleStart(pane.VisibleStartLine, totalTraceLinesKnown, traceCapacity);

                traceLines = SlicePreviewTraceLines(pane.PreviewLines, dataStartAbsoluteLine + pane.VisibleStartLine, traceCapacity);
            }

            if (pane.LoadVersion != loadVersion || !ReferenceEquals(pane.Document, document))
            {
                return;
            }

            List<string> displayLines = new(fixedHeaderCount + traceLines.Count);
            if (showHeader && pane.LogData.HeaderLine is not null)
            {
                displayLines.Add(pane.LogData.HeaderLine);
            }
            displayLines.AddRange(traceLines);

            SetListBoxItems(pane.ListBox, displayLines);

            if (traceLines.Count > 0)
            {
                int centeredTraceOffset = traceLines.Count / 2;
                int selectedIndex = fixedHeaderCount + centeredTraceOffset;
                if (selectedIndex >= 0 && selectedIndex < pane.ListBox.Items.Count)
                {
                    pane.ListBox.SelectedIndex = selectedIndex;
                    SetCurrentLineText(pane, dataStartAbsoluteLine + pane.VisibleStartLine + centeredTraceOffset);
                }
            }
            else
            {
                pane.ListBox.ClearSelected();
                SetCurrentLineText(pane, null);
            }
        }

        private static int ClampVisibleStart(int requestedStart, int totalTraceLines, int traceCapacity)
        {
            if (totalTraceLines <= 0)
            {
                return 0;
            }

            int maxStart = Math.Max(0, totalTraceLines - traceCapacity);
            return Math.Clamp(requestedStart, 0, maxStart);
        }

        private static IReadOnlyList<string> SlicePreviewTraceLines(
            IReadOnlyList<string> rawPreviewLines,
            int absoluteStartLine,
            int lineCount)
        {
            if (lineCount <= 0 || absoluteStartLine >= rawPreviewLines.Count)
            {
                return Array.Empty<string>();
            }

            int count = Math.Min(lineCount, rawPreviewLines.Count - absoluteStartLine);
            List<string> result = new(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(rawPreviewLines[absoluteStartLine + i]);
            }

            return result;
        }

        private void SetCurrentLineText(LogPaneState pane, int? absoluteLineIndex)
        {
            TextBox target = ReferenceEquals(pane, _primaryPane) ? txtCurrentLine : txtCurrentLine2;
            target.Text = absoluteLineIndex is int line
                ? (line + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
        }

        private void OnPreviewSelectionChanged(LogPaneState pane)
        {
            if (pane.ListBox.SelectedIndex < 0)
            {
                pane.LogData.SelectedLine.Clear();
                PopulateRegisterBoxes(pane);
                return;
            }

            if (pane.ListBox.SelectedItem is not string selectedText)
            {
                pane.LogData.SelectedLine.Clear();
                PopulateRegisterBoxes(pane);
                return;
            }

            if (IsHeaderDisplaySelection(pane, pane.ListBox.SelectedIndex))
            {
                pane.LogData.SelectedLine.Clear();
                PopulateRegisterBoxes(pane);
                return;
            }

            if (!pane.LogData.HasTemplateHeader)
            {
                pane.LogData.SelectedLine.Clear();
                PopulateRegisterBoxes(pane);
                return;
            }

            if (pane.LogData.OrderDefinition.Items.Count == 0)
            {
                pane.LogData.SelectedLine.Clear();
                PopulateRegisterBoxes(pane);
                AppendVerboseLog($"[{DateTime.Now:HH:mm:ss}] {pane.BaseTitle} selected line parse skipped: ORDER list is empty.{Environment.NewLine}{Environment.NewLine}");
                return;
            }

            IReadOnlyList<TraceLogOrderFieldSpec> orderItems = pane.LogData.OrderDefinition.Items;
            bool parsedOk = TryParseTracePreviewLineByOrder(
                selectedText,
                orderItems,
                pane.LogData.Opts.IsTabbed,
                out List<string> values,
                out string? parseError);

            StringBuilder sb = new();
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] {pane.BaseTitle} Selected Preview Item");
            sb.AppendLine($"ParseMode: {(pane.LogData.Opts.IsTabbed ? "ORDER + TAB" : "ORDER + SPACE+")}");
            sb.AppendLine($"OrderCount: {orderItems.Count}");
            sb.AppendLine($"ValueCount: {values.Count}");

            if (!parsedOk || values.Count != orderItems.Count)
            {
                pane.LogData.SelectedLine.Clear();
                sb.AppendLine("ERROR: Parsed value count does not match ORDER item count.");
                if (!string.IsNullOrWhiteSpace(parseError))
                {
                    sb.AppendLine($"Reason: {parseError}");
                }
                sb.AppendLine($"Line: {selectedText}");
            }
            else
            {
                pane.LogData.SelectedLine.Clear();

                for (int i = 0; i < orderItems.Count; i++)
                {
                    TraceLogOrderFieldSpec item = orderItems[i];
                    sb.AppendLine($"  [{i:00}] {TraceLogOrderParser.Describe(item)} => \"{values[i]}\"");
                }

                ApplyParsedValuesToSelectedLine(pane, orderItems, values, sb);
            }

            sb.AppendLine();
            AppendVerboseLog(sb.ToString());
            PopulateRegisterBoxes(pane);
        }

        private static bool IsHeaderDisplaySelection(LogPaneState pane, int selectedIndex)
        {
            if (!pane.LogData.HasTemplateHeader)
            {
                return false;
            }

            if (string.IsNullOrEmpty(pane.LogData.HeaderLine))
            {
                return false;
            }

            bool hideHeaderLine = pane.HeaderVisibilityCheckBox?.Checked == true;
            return !hideHeaderLine && selectedIndex == 0;
        }

        private static bool TryParseTracePreviewLineByOrder(
            string line,
            IReadOnlyList<TraceLogOrderFieldSpec> orderItems,
            bool isTabbed,
            out List<string> values,
            out string? error)
        {
            values = new List<string>(orderItems.Count);
            error = null;
            int cursor = 0;

            for (int i = 0; i < orderItems.Count; i++)
            {
                TraceLogOrderFieldSpec item = orderItems[i];

                if (item.FixedWidth is int fixedWidth && fixedWidth > 0)
                {
                    if (!TryReadFixedWidthField(line, ref cursor, fixedWidth, out string fixedValue, out error))
                    {
                        error = $"Item[{i}] {TraceLogOrderParser.Describe(item)} fixed-width parse failed. {error}";
                        return false;
                    }

                    values.Add(fixedValue);
                    continue;
                }

                if (IsMem4OrderItem(item))
                {
                    if (!TryReadMem4Field(line, ref cursor, out string mem4Value, out error))
                    {
                        error = $"Item[{i}] {TraceLogOrderParser.Describe(item)} MEM4 parse failed. {error}";
                        return false;
                    }

                    values.Add(mem4Value);
                    continue;
                }

                if (!TryReadDelimitedField(line, ref cursor, isTabbed, out string value, out error))
                {
                    error = $"Item[{i}] {TraceLogOrderParser.Describe(item)} parse failed. {error}";
                    return false;
                }

                values.Add(value);
            }

            return true;
        }

        private static bool TryReadFixedWidthField(
            string line,
            ref int cursor,
            int width,
            out string value,
            out string? error)
        {
            value = string.Empty;
            error = null;

            if (width <= 0)
            {
                error = $"Invalid width {width}.";
                return false;
            }

            if (cursor > line.Length)
            {
                error = "Cursor is beyond end of line.";
                return false;
            }

            if (cursor + width > line.Length)
            {
                error = $"Line too short. Need {width} chars at position {cursor}, remaining {line.Length - cursor}.";
                return false;
            }

            value = line.Substring(cursor, width).Trim();
            cursor += width;
            return true;
        }

        private static bool TryReadMem4Field(string line, ref int cursor, out string value, out string? error)
        {
            value = string.Empty;
            error = null;
            List<string> parts = new(4);

            for (int i = 0; i < 4; i++)
            {
                if (!TryReadWhitespaceDelimitedToken(line, ref cursor, out string part, out error))
                {
                    error = $"MEM4 part {i + 1}/4 missing. {error}";
                    return false;
                }

                parts.Add(part);
            }

            value = string.Join(' ', parts);
            return true;
        }

        private static bool TryReadDelimitedField(
            string line,
            ref int cursor,
            bool isTabbed,
            out string value,
            out string? error)
        {
            if (isTabbed)
            {
                return TryReadTabDelimitedToken(line, ref cursor, out value, out error);
            }

            return TryReadWhitespaceDelimitedToken(line, ref cursor, out value, out error);
        }

        private static bool TryReadWhitespaceDelimitedToken(
            string line,
            ref int cursor,
            out string value,
            out string? error)
        {
            value = string.Empty;
            error = null;

            while (cursor < line.Length && char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            if (cursor >= line.Length)
            {
                error = "Reached end of line while expecting a token.";
                return false;
            }

            int start = cursor;
            while (cursor < line.Length && !char.IsWhiteSpace(line[cursor]))
            {
                cursor++;
            }

            value = line.Substring(start, cursor - start).Trim();
            return true;
        }

        private static bool TryReadTabDelimitedToken(
            string line,
            ref int cursor,
            out string value,
            out string? error)
        {
            value = string.Empty;
            error = null;

            // Consume only one column separator. Consecutive tabs represent empty fields.
            if (cursor < line.Length && line[cursor] == '\t')
            {
                cursor++;
            }

            if (cursor > line.Length)
            {
                error = "Reached end of line while expecting a tab-delimited token.";
                return false;
            }

            if (cursor == line.Length)
            {
                value = string.Empty;
                return true;
            }

            int start = cursor;
            while (cursor < line.Length && line[cursor] != '\t')
            {
                cursor++;
            }

            value = line.Substring(start, cursor - start).Trim();
            return true;
        }

        private static bool IsMem4OrderItem(TraceLogOrderFieldSpec item)
        {
            if (item.Kind != TraceLogOrderItemKind.Field)
            {
                return false;
            }

            if (item.Field == TraceLogOrderField.OpcodeValue)
            {
                return true;
            }

            return item.NormalizedToken.Equals("MEM4", StringComparison.OrdinalIgnoreCase);
        }

        private void PopulateRegisterBoxes(LogPaneState pane)
        {
            bool isPrimary = ReferenceEquals(pane, _primaryPane);
            TraceLogData logData = pane.LogData;
            TraceLogRegisters r = logData.SelectedLine.Registers;

            ushort? af = r.AF ?? ComposePair(r.A, r.F);
            ushort? bc = r.BC ?? ComposePair(r.B, r.C);
            ushort? de = r.DE ?? ComposePair(r.D, r.E);
            ushort? hl = r.HL ?? ComposePair(r.H, r.L);
            byte? fValue = r.F ?? ExtractLowByte(r.AF);

            if (isPrimary)
            {
                SetRegisterTextBox(txtPC1, FormatUInt16(r.PC, logData));
                SetRegisterTextBox(txtSP1, FormatUInt16(r.SP, logData));
                SetRegisterTextBox(txtAF1, FormatUInt16(af, logData));
                SetRegisterTextBox(txtBC1, FormatUInt16(bc, logData));
                SetRegisterTextBox(txtDE1, FormatUInt16(de, logData));
                SetRegisterTextBox(txtHL1, FormatUInt16(hl, logData));
                SetRegisterTextBox(txtIX1, FormatUInt16(r.IX, logData));
                SetRegisterTextBox(txtIY1, FormatUInt16(r.IY, logData));
                SetRegisterTextBox(txtIR1, FormatUInt16(r.IR, logData));
                SetRegisterTextBox(txtIM1, FormatByte(r.IM, logData));
                SetRegisterTextBox(txtAFPrime1, FormatUInt16(r.AFx, logData));
                SetRegisterTextBox(txtBCPrime1, FormatUInt16(r.BCx, logData));
                SetRegisterTextBox(txtDEPrime1, FormatUInt16(r.DEx, logData));
                SetRegisterTextBox(txtHLPrime1, FormatUInt16(r.HLx, logData));
                SetRegisterTextBox(txtWZ1, FormatUInt16(r.WZ, logData));
                SetRegisterTextBox(txtPG1, FormatByte(logData.SelectedLine.Port7FFD, logData));
                SetFlagCheckboxesPrimary(fValue);
            }
            else
            {
                SetRegisterTextBox(txtPC2, FormatUInt16(r.PC, logData));
                SetRegisterTextBox(txtSP2, FormatUInt16(r.SP, logData));
                SetRegisterTextBox(txtAF2, FormatUInt16(af, logData));
                SetRegisterTextBox(txtBC2, FormatUInt16(bc, logData));
                SetRegisterTextBox(txtDE2, FormatUInt16(de, logData));
                SetRegisterTextBox(txtHL2, FormatUInt16(hl, logData));
                SetRegisterTextBox(txtIX2, FormatUInt16(r.IX, logData));
                SetRegisterTextBox(txtIY2, FormatUInt16(r.IY, logData));
                SetRegisterTextBox(txtIR2, FormatUInt16(r.IR, logData));
                SetRegisterTextBox(txtIM2, FormatByte(r.IM, logData));
                SetRegisterTextBox(txtAFPrime2, FormatUInt16(r.AFx, logData));
                SetRegisterTextBox(txtBCPrime2, FormatUInt16(r.BCx, logData));
                SetRegisterTextBox(txtDEPrime2, FormatUInt16(r.DEx, logData));
                SetRegisterTextBox(txtHLPrime2, FormatUInt16(r.HLx, logData));
                SetRegisterTextBox(txtWZ2, FormatUInt16(r.WZ, logData));
                SetRegisterTextBox(txtPG2, FormatByte(logData.SelectedLine.Port7FFD, logData));
                SetFlagCheckboxesSecondary(fValue);
            }

            UpdateCrossPaneRegisterDifferenceColors();
        }

        private static ushort? ComposePair(byte? high, byte? low)
        {
            if (high is null || low is null)
            {
                return null;
            }

            return (ushort)((high.Value << 8) | low.Value);
        }

        private static byte? ExtractLowByte(ushort? value)
        {
            return value is null ? null : (byte)(value.Value & 0xFF);
        }

        private void SetRegisterTextBox(TextBox textBox, string text)
        {
            bool changed = !string.Equals(textBox.Text, text, StringComparison.Ordinal);
            textBox.Text = text;
            textBox.BackColor = changed ? _registerChangedBackColor : _registerDefaultBackColor;
        }

        private void UpdateCrossPaneRegisterDifferenceColors()
        {
            bool canCompare = _primaryPane.Document is not null &&
                _secondaryPane.Document is not null &&
                _primaryPane.ListBox.SelectedIndex >= 0 &&
                _secondaryPane.ListBox.SelectedIndex >= 0;

            ApplyCrossPaneDiffColor(txtPC1, txtPC2, canCompare);
            ApplyCrossPaneDiffColor(txtSP1, txtSP2, canCompare);
            ApplyCrossPaneDiffColor(txtAF1, txtAF2, canCompare);
            ApplyCrossPaneDiffColor(txtBC1, txtBC2, canCompare);
            ApplyCrossPaneDiffColor(txtDE1, txtDE2, canCompare);
            ApplyCrossPaneDiffColor(txtHL1, txtHL2, canCompare);
            ApplyCrossPaneDiffColor(txtIX1, txtIX2, canCompare);
            ApplyCrossPaneDiffColor(txtIY1, txtIY2, canCompare);
            ApplyCrossPaneDiffColor(txtIR1, txtIR2, canCompare);
            ApplyCrossPaneDiffColor(txtIM1, txtIM2, canCompare);
            ApplyCrossPaneDiffColor(txtAFPrime1, txtAFPrime2, canCompare);
            ApplyCrossPaneDiffColor(txtBCPrime1, txtBCPrime2, canCompare);
            ApplyCrossPaneDiffColor(txtDEPrime1, txtDEPrime2, canCompare);
            ApplyCrossPaneDiffColor(txtHLPrime1, txtHLPrime2, canCompare);
            ApplyCrossPaneDiffColor(txtWZ1, txtWZ2, canCompare);
            ApplyCrossPaneDiffColor(txtPG1, txtPG2, canCompare);
        }

        private void ApplyCrossPaneDiffColor(TextBox primary, TextBox secondary, bool canCompare)
        {
            bool different = canCompare &&
                !string.Equals(primary.Text, secondary.Text, StringComparison.Ordinal);

            Color color = different ? _registerDiffForeColor : _registerDefaultForeColor;
            primary.ForeColor = color;
            secondary.ForeColor = color;
        }

        private void SetFlagCheckboxesPrimary(byte? fValue)
        {
            SetFlagCheckboxes(fValue, chkS1, chkZ1, chk51, chkH1, chk31, chkV1, chkN1, chkC1);
        }

        private void SetFlagCheckboxesSecondary(byte? fValue)
        {
            SetFlagCheckboxes(fValue, chkS2, chkZ2, chk52, chkH2, chk32, chkV2, chkN2, chkC2);
        }

        private static void SetFlagCheckboxes(
            byte? fValue,
            CheckBox chkS,
            CheckBox chkZ,
            CheckBox chk5,
            CheckBox chkH,
            CheckBox chk3,
            CheckBox chkV,
            CheckBox chkN,
            CheckBox chkC)
        {
            byte flags = fValue ?? 0;
            bool hasValue = fValue is not null;

            chkS.Checked = hasValue && (flags & 0x80) != 0;
            chkZ.Checked = hasValue && (flags & 0x40) != 0;
            chk5.Checked = hasValue && (flags & 0x20) != 0;
            chkH.Checked = hasValue && (flags & 0x10) != 0;
            chk3.Checked = hasValue && (flags & 0x08) != 0;
            chkV.Checked = hasValue && (flags & 0x04) != 0;
            chkN.Checked = hasValue && (flags & 0x02) != 0;
            chkC.Checked = hasValue && (flags & 0x01) != 0;
        }

        private static string FormatUInt16(ushort? value, TraceLogData logData)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (logData.Opts.Hex)
            {
                string prefix = logData.Opts.HexPrefix ?? string.Empty;
                return $"{prefix}{value.Value:X4}";
            }

            return value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private static string FormatByte(byte? value, TraceLogData logData)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (logData.Opts.Hex)
            {
                string prefix = logData.Opts.HexPrefix ?? string.Empty;
                return $"{prefix}{value.Value:X2}";
            }

            return value.Value.ToString(CultureInfo.InvariantCulture);
        }

        private void ApplyParsedValuesToSelectedLine(
            LogPaneState pane,
            IReadOnlyList<TraceLogOrderFieldSpec> orderItems,
            IReadOnlyList<string> values,
            StringBuilder debugLog)
        {
            RegisterValueParseMode defaultRegisterParseMode = pane.LogData.Opts.Hex
                ? RegisterValueParseMode.Hex
                : RegisterValueParseMode.Decimal;

            bool wroteAnyRegister = false;

            for (int i = 0; i < orderItems.Count; i++)
            {
                TraceLogOrderFieldSpec item = orderItems[i];
                string rawValue = values[i];

                if (item.Kind == TraceLogOrderItemKind.FormatDirective &&
                    item.FormatTarget == TraceLogOrderFormatTarget.Registers)
                {
                    RegisterValueParseMode directiveParseMode = item.ValueFormat switch
                    {
                        TraceLogOrderValueFormat.Hex => RegisterValueParseMode.Hex,
                        TraceLogOrderValueFormat.Decimal => RegisterValueParseMode.Decimal,
                        _ => defaultRegisterParseMode
                    };

                    if (TryAssignDynamicRegisterDirectiveValue(
                        pane.LogData.SelectedLine.Registers,
                        rawValue,
                        directiveParseMode,
                        pane.LogData.Opts.HexPrefix,
                        out string? dynamicError))
                    {
                        wroteAnyRegister = true;
                    }
                    else
                    {
                        debugLog.AppendLine($"  ERR [{i:00}] {TraceLogOrderParser.Describe(item)} <= \"{rawValue}\" :: {dynamicError}");
                    }

                    continue;
                }

                if (item.Kind != TraceLogOrderItemKind.Register)
                {
                    if (item.Kind == TraceLogOrderItemKind.Field &&
                        item.Field == TraceLogOrderField.Address)
                    {
                        if (TryAssignRegisterFromString(
                            pane.LogData.SelectedLine.Registers,
                            TraceLogOrderField.PC,
                            rawValue,
                            defaultRegisterParseMode,
                            pane.LogData.Opts.HexPrefix,
                            out string? addressError))
                        {
                            wroteAnyRegister = true;
                        }
                        else
                        {
                            debugLog.AppendLine($"  ERR [{i:00}] {TraceLogOrderParser.Describe(item)} <= \"{rawValue}\" :: {addressError}");
                        }
                    }

                    continue;
                }

                if (TryAssignRegisterFromString(
                    pane.LogData.SelectedLine.Registers,
                    item.Field,
                    rawValue,
                    defaultRegisterParseMode,
                    pane.LogData.Opts.HexPrefix,
                    out string? error))
                {
                    wroteAnyRegister = true;
                    continue;
                }

                debugLog.AppendLine($"  ERR [{i:00}] {TraceLogOrderParser.Describe(item)} <= \"{rawValue}\" :: {error}");
            }

            if (wroteAnyRegister)
            {
                debugLog.AppendLine("Registers: parsed values applied to SelectedLine.Registers");
            }
        }

        private static bool TryAssignDynamicRegisterDirectiveValue(
            TraceLogRegisters registers,
            string rawValue,
            RegisterValueParseMode parseMode,
            string? hexPrefix,
            out string? error)
        {
            error = null;

            int equalsIndex = rawValue.IndexOf('=');
            if (equalsIndex <= 0 || equalsIndex == rawValue.Length - 1)
            {
                error = "Expected 'Register=Value' format.";
                return false;
            }

            string rawRegisterName = rawValue[..equalsIndex].Trim();
            string rawRegisterValue = rawValue[(equalsIndex + 1)..].Trim();

            if (rawRegisterName.Length == 0)
            {
                error = "Register name is empty.";
                return false;
            }

            if (rawRegisterValue.Length == 0)
            {
                error = "Register value is empty.";
                return false;
            }

            if (!TryResolveDynamicRegisterTarget(rawRegisterName, out DynamicRegisterTarget target, out string? resolveError))
            {
                error = resolveError;
                return false;
            }

            if (!TryParseIntegerText(rawRegisterValue, parseMode, hexPrefix, out ulong numericValue, out error))
            {
                ClearDynamicRegisterTarget(registers, target);
                return false;
            }

            if (!TryAssignDynamicRegisterTarget(registers, target, numericValue, out error))
            {
                ClearDynamicRegisterTarget(registers, target);
                return false;
            }

            return true;
        }

        private static bool TryAssignRegisterFromString(
            TraceLogRegisters registers,
            TraceLogOrderField field,
            string rawValue,
            RegisterValueParseMode parseMode,
            string? hexPrefix,
            out string? error)
        {
            error = null;

            if (!TryParseIntegerText(rawValue, parseMode, hexPrefix, out ulong numericValue, out error))
            {
                SetRegisterFieldNull(registers, field);
                return false;
            }

            switch (field)
            {
                // 8-bit
                case TraceLogOrderField.A:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.AF_High_A,
                        numericValue,
                        out error);
                case TraceLogOrderField.F:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.AF_Low_F,
                        numericValue,
                        out error);
                case TraceLogOrderField.B:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.BC_High_B,
                        numericValue,
                        out error);
                case TraceLogOrderField.C:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.BC_Low_C,
                        numericValue,
                        out error);
                case TraceLogOrderField.D:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.DE_High_D,
                        numericValue,
                        out error);
                case TraceLogOrderField.E:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.DE_Low_E,
                        numericValue,
                        out error);
                case TraceLogOrderField.H:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.HL_High_H,
                        numericValue,
                        out error);
                case TraceLogOrderField.L:
                    return TryAssignPairByte(
                        registers,
                        PairByteTarget.HL_Low_L,
                        numericValue,
                        out error);
                case TraceLogOrderField.IM:
                    return TryAssignByte(v => registers.IM = v, () => registers.IM = null, numericValue, out error);

                // 16-bit
                case TraceLogOrderField.PC:
                    return TryAssignUShort(v => registers.PC = v, () => registers.PC = null, numericValue, out error);
                case TraceLogOrderField.SP:
                    return TryAssignUShort(v => registers.SP = v, () => registers.SP = null, numericValue, out error);
                case TraceLogOrderField.AF:
                    return TryAssignUShort(v => registers.AF = v, () => registers.AF = null, numericValue, out error);
                case TraceLogOrderField.BC:
                    return TryAssignUShort(v => registers.BC = v, () => registers.BC = null, numericValue, out error);
                case TraceLogOrderField.DE:
                    return TryAssignUShort(v => registers.DE = v, () => registers.DE = null, numericValue, out error);
                case TraceLogOrderField.HL:
                    return TryAssignUShort(v => registers.HL = v, () => registers.HL = null, numericValue, out error);
                case TraceLogOrderField.IX:
                    return TryAssignUShort(v => registers.IX = v, () => registers.IX = null, numericValue, out error);
                case TraceLogOrderField.IY:
                    return TryAssignUShort(v => registers.IY = v, () => registers.IY = null, numericValue, out error);
                case TraceLogOrderField.IR:
                    return TryAssignUShort(v => registers.IR = v, () => registers.IR = null, numericValue, out error);
                case TraceLogOrderField.AFx:
                    return TryAssignUShort(v => registers.AFx = v, () => registers.AFx = null, numericValue, out error);
                case TraceLogOrderField.BCx:
                    return TryAssignUShort(v => registers.BCx = v, () => registers.BCx = null, numericValue, out error);
                case TraceLogOrderField.DEx:
                    return TryAssignUShort(v => registers.DEx = v, () => registers.DEx = null, numericValue, out error);
                case TraceLogOrderField.HLx:
                    return TryAssignUShort(v => registers.HLx = v, () => registers.HLx = null, numericValue, out error);
                case TraceLogOrderField.WZ:
                    return TryAssignUShort(v => registers.WZ = v, () => registers.WZ = null, numericValue, out error);

                // Bool-like registers
                case TraceLogOrderField.IFF1:
                    return TryAssignBool(v => registers.IFF1 = v, () => registers.IFF1 = null, numericValue, out error);
                case TraceLogOrderField.IFF2:
                    return TryAssignBool(v => registers.IFF2 = v, () => registers.IFF2 = null, numericValue, out error);

                default:
                    error = $"Register field '{field}' is not supported by register assignment.";
                    return false;
            }
        }

        private static bool TryParseIntegerText(
            string rawValue,
            RegisterValueParseMode parseMode,
            string? hexPrefix,
            out ulong value,
            out string? error)
        {
            value = 0;
            error = null;

            string text = rawValue.Trim();
            if (text.Length == 0)
            {
                error = "Empty value.";
                return false;
            }

            if (parseMode == RegisterValueParseMode.Hex)
            {
                string normalized = text;

                if (!string.IsNullOrEmpty(hexPrefix) &&
                    normalized.StartsWith(hexPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized[hexPrefix.Length..].Trim();
                }

                if (normalized.Length == 0)
                {
                    error = "Hex value is empty after prefix removal.";
                    return false;
                }

                if (!ulong.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value))
                {
                    error = $"Invalid hex value '{rawValue}'.";
                    return false;
                }

                return true;
            }

            if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                error = $"Invalid decimal value '{rawValue}'.";
                return false;
            }

            return true;
        }

        private static bool TryAssignByte(Action<byte> assign, Action clear, ulong value, out string? error)
        {
            if (value > byte.MaxValue)
            {
                clear();
                error = $"Value out of range for byte: {value}.";
                return false;
            }

            assign((byte)value);
            error = null;
            return true;
        }

        private static bool TryAssignUShort(Action<ushort> assign, Action clear, ulong value, out string? error)
        {
            if (value > ushort.MaxValue)
            {
                clear();
                error = $"Value out of range for ushort: {value}.";
                return false;
            }

            assign((ushort)value);
            error = null;
            return true;
        }

        private static bool TryAssignBool(Action<bool> assign, Action clear, ulong value, out string? error)
        {
            if (value > 1)
            {
                clear();
                error = $"Value out of range for bool register (expected 0 or 1): {value}.";
                return false;
            }

            assign(value != 0);
            error = null;
            return true;
        }

        private static bool TryAssignPairByte(
            TraceLogRegisters registers,
            PairByteTarget target,
            ulong value,
            out string? error)
        {
            error = null;

            if (value > byte.MaxValue)
            {
                ClearPairByteTarget(registers, target);
                error = $"Value out of range for byte: {value}.";
                return false;
            }

            byte byteValue = (byte)value;
            switch (target)
            {
                case PairByteTarget.AF_High_A:
                    registers.A = byteValue;
                    registers.AF = ComposeUpdatedPair(registers.AF, registers.F, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.AF_Low_F:
                    registers.F = byteValue;
                    registers.AF = ComposeUpdatedPair(registers.AF, registers.A, byteValue, isHighByte: false);
                    return true;
                case PairByteTarget.BC_High_B:
                    registers.B = byteValue;
                    registers.BC = ComposeUpdatedPair(registers.BC, registers.C, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.BC_Low_C:
                    registers.C = byteValue;
                    registers.BC = ComposeUpdatedPair(registers.BC, registers.B, byteValue, isHighByte: false);
                    return true;
                case PairByteTarget.DE_High_D:
                    registers.D = byteValue;
                    registers.DE = ComposeUpdatedPair(registers.DE, registers.E, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.DE_Low_E:
                    registers.E = byteValue;
                    registers.DE = ComposeUpdatedPair(registers.DE, registers.D, byteValue, isHighByte: false);
                    return true;
                case PairByteTarget.HL_High_H:
                    registers.H = byteValue;
                    registers.HL = ComposeUpdatedPair(registers.HL, registers.L, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.HL_Low_L:
                    registers.L = byteValue;
                    registers.HL = ComposeUpdatedPair(registers.HL, registers.H, byteValue, isHighByte: false);
                    return true;
                case PairByteTarget.IR_High_I:
                    registers.IR = ComposeUpdatedPair(registers.IR, fallbackOtherByte: null, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.IR_Low_R:
                    registers.IR = ComposeUpdatedPair(registers.IR, fallbackOtherByte: null, byteValue, isHighByte: false);
                    return true;
                case PairByteTarget.IX_High_XH:
                    registers.IX = ComposeUpdatedPair(registers.IX, fallbackOtherByte: null, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.IX_Low_XL:
                    registers.IX = ComposeUpdatedPair(registers.IX, fallbackOtherByte: null, byteValue, isHighByte: false);
                    return true;
                case PairByteTarget.IY_High_YH:
                    registers.IY = ComposeUpdatedPair(registers.IY, fallbackOtherByte: null, byteValue, isHighByte: true);
                    return true;
                case PairByteTarget.IY_Low_YL:
                    registers.IY = ComposeUpdatedPair(registers.IY, fallbackOtherByte: null, byteValue, isHighByte: false);
                    return true;
                default:
                    error = $"Unsupported pair-byte target '{target}'.";
                    return false;
            }
        }

        private static ushort ComposeUpdatedPair(ushort? currentPair, byte? fallbackOtherByte, byte newByte, bool isHighByte)
        {
            byte otherByte;

            if (fallbackOtherByte is not null)
            {
                otherByte = fallbackOtherByte.Value;
            }
            else if (currentPair is not null)
            {
                otherByte = isHighByte
                    ? (byte)(currentPair.Value & 0xFF)
                    : (byte)((currentPair.Value >> 8) & 0xFF);
            }
            else
            {
                otherByte = 0;
            }

            return isHighByte
                ? (ushort)((newByte << 8) | otherByte)
                : (ushort)((otherByte << 8) | newByte);
        }

        private static void ClearPairByteTarget(TraceLogRegisters registers, PairByteTarget target)
        {
            switch (target)
            {
                case PairByteTarget.AF_High_A:
                    registers.A = null;
                    registers.AF = null;
                    break;
                case PairByteTarget.AF_Low_F:
                    registers.F = null;
                    registers.AF = null;
                    break;
                case PairByteTarget.BC_High_B:
                    registers.B = null;
                    registers.BC = null;
                    break;
                case PairByteTarget.BC_Low_C:
                    registers.C = null;
                    registers.BC = null;
                    break;
                case PairByteTarget.DE_High_D:
                    registers.D = null;
                    registers.DE = null;
                    break;
                case PairByteTarget.DE_Low_E:
                    registers.E = null;
                    registers.DE = null;
                    break;
                case PairByteTarget.HL_High_H:
                    registers.H = null;
                    registers.HL = null;
                    break;
                case PairByteTarget.HL_Low_L:
                    registers.L = null;
                    registers.HL = null;
                    break;
                case PairByteTarget.IR_High_I:
                case PairByteTarget.IR_Low_R:
                    registers.IR = null;
                    break;
                case PairByteTarget.IX_High_XH:
                case PairByteTarget.IX_Low_XL:
                    registers.IX = null;
                    break;
                case PairByteTarget.IY_High_YH:
                case PairByteTarget.IY_Low_YL:
                    registers.IY = null;
                    break;
            }
        }

        private static void SetRegisterFieldNull(TraceLogRegisters registers, TraceLogOrderField field)
        {
            switch (field)
            {
                case TraceLogOrderField.A: registers.A = null; registers.AF = null; break;
                case TraceLogOrderField.F: registers.F = null; registers.AF = null; break;
                case TraceLogOrderField.B: registers.B = null; registers.BC = null; break;
                case TraceLogOrderField.C: registers.C = null; registers.BC = null; break;
                case TraceLogOrderField.D: registers.D = null; registers.DE = null; break;
                case TraceLogOrderField.E: registers.E = null; registers.DE = null; break;
                case TraceLogOrderField.H: registers.H = null; registers.HL = null; break;
                case TraceLogOrderField.L: registers.L = null; registers.HL = null; break;
                case TraceLogOrderField.IM: registers.IM = null; break;

                case TraceLogOrderField.PC: registers.PC = null; break;
                case TraceLogOrderField.SP: registers.SP = null; break;
                case TraceLogOrderField.AF: registers.AF = null; break;
                case TraceLogOrderField.BC: registers.BC = null; break;
                case TraceLogOrderField.DE: registers.DE = null; break;
                case TraceLogOrderField.HL: registers.HL = null; break;
                case TraceLogOrderField.IX: registers.IX = null; break;
                case TraceLogOrderField.IY: registers.IY = null; break;
                case TraceLogOrderField.IR: registers.IR = null; break;
                case TraceLogOrderField.AFx: registers.AFx = null; break;
                case TraceLogOrderField.BCx: registers.BCx = null; break;
                case TraceLogOrderField.DEx: registers.DEx = null; break;
                case TraceLogOrderField.HLx: registers.HLx = null; break;
                case TraceLogOrderField.WZ: registers.WZ = null; break;

                case TraceLogOrderField.IFF1: registers.IFF1 = null; break;
                case TraceLogOrderField.IFF2: registers.IFF2 = null; break;
            }
        }

        private static bool TryResolveDynamicRegisterTarget(
            string rawRegisterName,
            out DynamicRegisterTarget target,
            out string? error)
        {
            target = default;
            error = null;

            string name = rawRegisterName.Trim();
            if (name.Length == 0)
            {
                error = "Empty register name.";
                return false;
            }

            string upper = name.ToUpperInvariant();

            // Alternate set shorthand like AF', BC*, DE?, HLx -> normalize to AFx/BCx/DEx/HLx.
            if (upper.Length == 3)
            {
                string firstTwo = upper[..2];
                if (firstTwo is "AF" or "BC" or "DE" or "HL")
                {
                    target = firstTwo switch
                    {
                        "AF" => DynamicRegisterTarget.FieldAFx,
                        "BC" => DynamicRegisterTarget.FieldBCx,
                        "DE" => DynamicRegisterTarget.FieldDEx,
                        "HL" => DynamicRegisterTarget.FieldHLx,
                        _ => default
                    };
                    return true;
                }
            }

            if (upper.Length > 3 && upper is not "IFF1" and not "IFF2")
            {
                error = $"Dynamic register name '{rawRegisterName}' is too long.";
                return false;
            }

            switch (upper)
            {
                case "PC": target = DynamicRegisterTarget.FieldPC; return true;
                case "SP": target = DynamicRegisterTarget.FieldSP; return true;
                case "A": target = DynamicRegisterTarget.PairByteAF_High_A; return true;
                case "F": target = DynamicRegisterTarget.PairByteAF_Low_F; return true;
                case "B": target = DynamicRegisterTarget.PairByteBC_High_B; return true;
                case "C": target = DynamicRegisterTarget.PairByteBC_Low_C; return true;
                case "D": target = DynamicRegisterTarget.PairByteDE_High_D; return true;
                case "E": target = DynamicRegisterTarget.PairByteDE_Low_E; return true;
                case "H": target = DynamicRegisterTarget.PairByteHL_High_H; return true;
                case "L": target = DynamicRegisterTarget.PairByteHL_Low_L; return true;
                case "BC": target = DynamicRegisterTarget.FieldBC; return true;
                case "DE": target = DynamicRegisterTarget.FieldDE; return true;
                case "HL": target = DynamicRegisterTarget.FieldHL; return true;
                case "AF": target = DynamicRegisterTarget.FieldAF; return true;
                case "IX":
                case "X":
                    target = DynamicRegisterTarget.FieldIX; return true;
                case "XH":
                    target = DynamicRegisterTarget.PairByteIX_High_XH; return true;
                case "XL":
                    target = DynamicRegisterTarget.PairByteIX_Low_XL; return true;
                case "IY":
                case "Y":
                    target = DynamicRegisterTarget.FieldIY; return true;
                case "YH":
                    target = DynamicRegisterTarget.PairByteIY_High_YH; return true;
                case "YL":
                    target = DynamicRegisterTarget.PairByteIY_Low_YL; return true;
                case "IR":
                    target = DynamicRegisterTarget.FieldIR; return true;
                case "I":
                    target = DynamicRegisterTarget.PairByteIR_High_I; return true;
                case "R":
                    target = DynamicRegisterTarget.PairByteIR_Low_R; return true;
                case "WZ":
                    target = DynamicRegisterTarget.FieldWZ; return true;
                case "IM":
                    target = DynamicRegisterTarget.FieldIM; return true;
                case "IFF1":
                    target = DynamicRegisterTarget.FieldIFF1; return true;
                case "IFF2":
                    target = DynamicRegisterTarget.FieldIFF2; return true;
                case "AFX":
                    target = DynamicRegisterTarget.FieldAFx; return true;
                case "BCX":
                    target = DynamicRegisterTarget.FieldBCx; return true;
                case "DEX":
                    target = DynamicRegisterTarget.FieldDEx; return true;
                case "HLX":
                    target = DynamicRegisterTarget.FieldHLx; return true;
                default:
                    error = $"Unknown dynamic register name '{rawRegisterName}'.";
                    return false;
            }
        }

        private static bool TryAssignDynamicRegisterTarget(
            TraceLogRegisters registers,
            DynamicRegisterTarget target,
            ulong numericValue,
            out string? error)
        {
            switch (target)
            {
                case DynamicRegisterTarget.FieldPC:
                    return TryAssignUShort(v => registers.PC = v, () => registers.PC = null, numericValue, out error);
                case DynamicRegisterTarget.FieldSP:
                    return TryAssignUShort(v => registers.SP = v, () => registers.SP = null, numericValue, out error);
                case DynamicRegisterTarget.FieldAF:
                    return TryAssignUShort(v => registers.AF = v, () => registers.AF = null, numericValue, out error);
                case DynamicRegisterTarget.FieldBC:
                    return TryAssignUShort(v => registers.BC = v, () => registers.BC = null, numericValue, out error);
                case DynamicRegisterTarget.FieldDE:
                    return TryAssignUShort(v => registers.DE = v, () => registers.DE = null, numericValue, out error);
                case DynamicRegisterTarget.FieldHL:
                    return TryAssignUShort(v => registers.HL = v, () => registers.HL = null, numericValue, out error);
                case DynamicRegisterTarget.FieldIX:
                    return TryAssignUShort(v => registers.IX = v, () => registers.IX = null, numericValue, out error);
                case DynamicRegisterTarget.FieldIY:
                    return TryAssignUShort(v => registers.IY = v, () => registers.IY = null, numericValue, out error);
                case DynamicRegisterTarget.FieldIR:
                    return TryAssignUShort(v => registers.IR = v, () => registers.IR = null, numericValue, out error);
                case DynamicRegisterTarget.FieldAFx:
                    return TryAssignUShort(v => registers.AFx = v, () => registers.AFx = null, numericValue, out error);
                case DynamicRegisterTarget.FieldBCx:
                    return TryAssignUShort(v => registers.BCx = v, () => registers.BCx = null, numericValue, out error);
                case DynamicRegisterTarget.FieldDEx:
                    return TryAssignUShort(v => registers.DEx = v, () => registers.DEx = null, numericValue, out error);
                case DynamicRegisterTarget.FieldHLx:
                    return TryAssignUShort(v => registers.HLx = v, () => registers.HLx = null, numericValue, out error);
                case DynamicRegisterTarget.FieldWZ:
                    return TryAssignUShort(v => registers.WZ = v, () => registers.WZ = null, numericValue, out error);
                case DynamicRegisterTarget.FieldIM:
                    return TryAssignByte(v => registers.IM = v, () => registers.IM = null, numericValue, out error);
                case DynamicRegisterTarget.FieldIFF1:
                    return TryAssignBool(v => registers.IFF1 = v, () => registers.IFF1 = null, numericValue, out error);
                case DynamicRegisterTarget.FieldIFF2:
                    return TryAssignBool(v => registers.IFF2 = v, () => registers.IFF2 = null, numericValue, out error);

                case DynamicRegisterTarget.PairByteAF_High_A:
                    return TryAssignPairByte(registers, PairByteTarget.AF_High_A, numericValue, out error);
                case DynamicRegisterTarget.PairByteAF_Low_F:
                    return TryAssignPairByte(registers, PairByteTarget.AF_Low_F, numericValue, out error);
                case DynamicRegisterTarget.PairByteBC_High_B:
                    return TryAssignPairByte(registers, PairByteTarget.BC_High_B, numericValue, out error);
                case DynamicRegisterTarget.PairByteBC_Low_C:
                    return TryAssignPairByte(registers, PairByteTarget.BC_Low_C, numericValue, out error);
                case DynamicRegisterTarget.PairByteDE_High_D:
                    return TryAssignPairByte(registers, PairByteTarget.DE_High_D, numericValue, out error);
                case DynamicRegisterTarget.PairByteDE_Low_E:
                    return TryAssignPairByte(registers, PairByteTarget.DE_Low_E, numericValue, out error);
                case DynamicRegisterTarget.PairByteHL_High_H:
                    return TryAssignPairByte(registers, PairByteTarget.HL_High_H, numericValue, out error);
                case DynamicRegisterTarget.PairByteHL_Low_L:
                    return TryAssignPairByte(registers, PairByteTarget.HL_Low_L, numericValue, out error);
                case DynamicRegisterTarget.PairByteIR_High_I:
                    return TryAssignPairByte(registers, PairByteTarget.IR_High_I, numericValue, out error);
                case DynamicRegisterTarget.PairByteIR_Low_R:
                    return TryAssignPairByte(registers, PairByteTarget.IR_Low_R, numericValue, out error);
                case DynamicRegisterTarget.PairByteIX_High_XH:
                    return TryAssignPairByte(registers, PairByteTarget.IX_High_XH, numericValue, out error);
                case DynamicRegisterTarget.PairByteIX_Low_XL:
                    return TryAssignPairByte(registers, PairByteTarget.IX_Low_XL, numericValue, out error);
                case DynamicRegisterTarget.PairByteIY_High_YH:
                    return TryAssignPairByte(registers, PairByteTarget.IY_High_YH, numericValue, out error);
                case DynamicRegisterTarget.PairByteIY_Low_YL:
                    return TryAssignPairByte(registers, PairByteTarget.IY_Low_YL, numericValue, out error);

                default:
                    error = $"Unsupported dynamic register target '{target}'.";
                    return false;
            }
        }

        private static void ClearDynamicRegisterTarget(TraceLogRegisters registers, DynamicRegisterTarget target)
        {
            switch (target)
            {
                case DynamicRegisterTarget.FieldPC: registers.PC = null; break;
                case DynamicRegisterTarget.FieldSP: registers.SP = null; break;
                case DynamicRegisterTarget.FieldAF: registers.AF = null; break;
                case DynamicRegisterTarget.FieldBC: registers.BC = null; break;
                case DynamicRegisterTarget.FieldDE: registers.DE = null; break;
                case DynamicRegisterTarget.FieldHL: registers.HL = null; break;
                case DynamicRegisterTarget.FieldIX: registers.IX = null; break;
                case DynamicRegisterTarget.FieldIY: registers.IY = null; break;
                case DynamicRegisterTarget.FieldIR: registers.IR = null; break;
                case DynamicRegisterTarget.FieldAFx: registers.AFx = null; break;
                case DynamicRegisterTarget.FieldBCx: registers.BCx = null; break;
                case DynamicRegisterTarget.FieldDEx: registers.DEx = null; break;
                case DynamicRegisterTarget.FieldHLx: registers.HLx = null; break;
                case DynamicRegisterTarget.FieldWZ: registers.WZ = null; break;
                case DynamicRegisterTarget.FieldIM: registers.IM = null; break;
                case DynamicRegisterTarget.FieldIFF1: registers.IFF1 = null; break;
                case DynamicRegisterTarget.FieldIFF2: registers.IFF2 = null; break;

                case DynamicRegisterTarget.PairByteAF_High_A:
                    ClearPairByteTarget(registers, PairByteTarget.AF_High_A); break;
                case DynamicRegisterTarget.PairByteAF_Low_F:
                    ClearPairByteTarget(registers, PairByteTarget.AF_Low_F); break;
                case DynamicRegisterTarget.PairByteBC_High_B:
                    ClearPairByteTarget(registers, PairByteTarget.BC_High_B); break;
                case DynamicRegisterTarget.PairByteBC_Low_C:
                    ClearPairByteTarget(registers, PairByteTarget.BC_Low_C); break;
                case DynamicRegisterTarget.PairByteDE_High_D:
                    ClearPairByteTarget(registers, PairByteTarget.DE_High_D); break;
                case DynamicRegisterTarget.PairByteDE_Low_E:
                    ClearPairByteTarget(registers, PairByteTarget.DE_Low_E); break;
                case DynamicRegisterTarget.PairByteHL_High_H:
                    ClearPairByteTarget(registers, PairByteTarget.HL_High_H); break;
                case DynamicRegisterTarget.PairByteHL_Low_L:
                    ClearPairByteTarget(registers, PairByteTarget.HL_Low_L); break;
                case DynamicRegisterTarget.PairByteIR_High_I:
                    ClearPairByteTarget(registers, PairByteTarget.IR_High_I); break;
                case DynamicRegisterTarget.PairByteIR_Low_R:
                    ClearPairByteTarget(registers, PairByteTarget.IR_Low_R); break;
                case DynamicRegisterTarget.PairByteIX_High_XH:
                    ClearPairByteTarget(registers, PairByteTarget.IX_High_XH); break;
                case DynamicRegisterTarget.PairByteIX_Low_XL:
                    ClearPairByteTarget(registers, PairByteTarget.IX_Low_XL); break;
                case DynamicRegisterTarget.PairByteIY_High_YH:
                    ClearPairByteTarget(registers, PairByteTarget.IY_High_YH); break;
                case DynamicRegisterTarget.PairByteIY_Low_YL:
                    ClearPairByteTarget(registers, PairByteTarget.IY_Low_YL); break;
            }
        }

        private enum PairByteTarget
        {
            AF_High_A,
            AF_Low_F,
            BC_High_B,
            BC_Low_C,
            DE_High_D,
            DE_Low_E,
            HL_High_H,
            HL_Low_L,
            IR_High_I,
            IR_Low_R,
            IX_High_XH,
            IX_Low_XL,
            IY_High_YH,
            IY_Low_YL
        }

        private enum DynamicRegisterTarget
        {
            FieldPC,
            FieldSP,
            FieldAF,
            FieldBC,
            FieldDE,
            FieldHL,
            FieldIX,
            FieldIY,
            FieldIR,
            FieldAFx,
            FieldBCx,
            FieldDEx,
            FieldHLx,
            FieldWZ,
            FieldIM,
            FieldIFF1,
            FieldIFF2,
            PairByteAF_High_A,
            PairByteAF_Low_F,
            PairByteBC_High_B,
            PairByteBC_Low_C,
            PairByteDE_High_D,
            PairByteDE_Low_E,
            PairByteHL_High_H,
            PairByteHL_Low_L,
            PairByteIR_High_I,
            PairByteIR_Low_R,
            PairByteIX_High_XH,
            PairByteIX_Low_XL,
            PairByteIY_High_YH,
            PairByteIY_Low_YL
        }

        private enum RegisterValueParseMode
        {
            Decimal = 0,
            Hex
        }

        private static TControl? FindControlRecursive<TControl>(Control root, string name)
            where TControl : Control
        {
            foreach (Control child in root.Controls)
            {
                if (child is TControl typed &&
                    string.Equals(child.Name, name, StringComparison.Ordinal))
                {
                    return typed;
                }

                if (FindControlRecursive<TControl>(child, name) is { } nested)
                {
                    return nested;
                }
            }

            return null;
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
            if (!IsVerboseDebugEnabled())
            {
                return;
            }

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
                sb.AppendLine($"Order.Items: {logData.OrderDefinition.Items.Count}");

                for (int i = 0; i < logData.OrderDefinition.Items.Count; i++)
                {
                    TraceLogOrderFieldSpec item = logData.OrderDefinition.Items[i];
                    sb.AppendLine($"  [{i:00}] {TraceLogOrderParser.Describe(item)}");
                }

                sb.AppendLine($"Opts.Model: {logData.Opts.Model}");
                sb.AppendLine($"Opts.Slice: {logData.Opts.Slice}");
                sb.AppendLine($"Opts.ViewMem: {logData.Opts.ViewMem}");
                sb.AppendLine($"Opts.Hex: {logData.Opts.Hex}");
                sb.AppendLine($"Opts.HexPrefix: {(string.IsNullOrEmpty(logData.Opts.HexPrefix) ? "None" : logData.Opts.HexPrefix)}");
                sb.AppendLine($"Opts.SnapshotFile: {logData.Opts.SnapshotFile ?? "(null)"}");
            }

            sb.AppendLine();
            AppendVerboseLog(sb.ToString());
        }

        private bool IsVerboseDebugEnabled()
        {
            return chkVerboseDebug.Checked;
        }

        private void AppendVerboseLog(string text)
        {
            if (!IsVerboseDebugEnabled())
            {
                return;
            }

            txtLog.AppendText(text);
        }

        private sealed class TemplateAvailableFieldEntry
        {
            public TemplateAvailableFieldEntry(int sourceIndex, TraceLogOrderFieldSpec item)
            {
                SourceIndex = sourceIndex;
                Item = item;
            }

            public int SourceIndex { get; }
            public TraceLogOrderFieldSpec Item { get; }
        }

        private sealed class TraceLogData
        {
            public TraceLogTemplateHeader? TemplateHeader { get; private set; }
            public TraceLogOptions Opts { get; private set; } = new();
            public TraceLogOrderDefinition OrderDefinition { get; } = new();
            public TraceLogLineData SelectedLine { get; } = new();
            public string? HeaderLine { get; set; }
            public bool HasTemplateHeader => TemplateHeader is not null;

            public void Reset()
            {
                TemplateHeader = null;
                Opts = new TraceLogOptions();
                OrderDefinition.Clear();
                SelectedLine.Clear();
                HeaderLine = null;
            }

            public void SetTemplateHeader(TraceLogTemplateHeader header)
            {
                TemplateHeader = header;
                Opts = TraceLogOptionsParser.ParseDefineLine(header.Options);
                TraceLogOrderParser.ParseInto(OrderDefinition, header.Order);
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
            public List<string> PreviewLines { get; } = new();
            public CheckBox? HeaderVisibilityCheckBox { get; set; }
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

            public Task<IReadOnlyList<string>> ReadLinesAsync(
                int startLineIndex,
                int lineCount,
                CancellationToken cancellationToken)
            {
                return Task.Run<IReadOnlyList<string>>(() =>
                {
                    if (lineCount <= 0 || startLineIndex < 0)
                    {
                        return Array.Empty<string>();
                    }

                    long[] offsets;
                    lock (_indexLock)
                    {
                        if (!IsLineIndexComplete || _lineStartOffsets is null)
                        {
                            throw new InvalidOperationException("Line index is not ready.");
                        }

                        offsets = _lineStartOffsets.ToArray();
                    }

                    if (startLineIndex >= offsets.Length)
                    {
                        return Array.Empty<string>();
                    }

                    int count = Math.Min(lineCount, offsets.Length - startLineIndex);
                    List<string> lines = new(count);

                    using FileStream stream = new(
                        FilePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        1024 * 1024,
                        FileOptions.SequentialScan);

                    stream.Seek(offsets[startLineIndex], SeekOrigin.Begin);

                    using StreamReader reader = new(
                        stream,
                        Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: startLineIndex == 0,
                        bufferSize: 1024 * 1024);

                    for (int i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.EndOfStream)
                        {
                            break;
                        }

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

        private void utilitiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            GroupUtilities.Visible = !GroupUtilities.Visible;
            if (GroupUtilities.Visible)
            {
                utilitiesToolStripMenuItem.Text = "Hide Utilities";
                groupTemplateEditor.Width = GroupUtilities.Left - 20;
                groupBoxPrimary.Width = GroupUtilities.Left - 20;
                groupTemplateEditor.Width = GroupUtilities.Left - 20;
                utilitiesToolStripMenuItem.Checked = true;
            }
            else
            {
                utilitiesToolStripMenuItem.Text = "Show Utilities";
                utilitiesToolStripMenuItem.Checked = false;
                groupTemplateEditor.Width = Form1.ActiveForm.ClientSize.Width - 20;
                groupBoxPrimary.Width = Form1.ActiveForm.ClientSize.Width - 20;
                groupTemplateEditor.Width = Form1.ActiveForm.ClientSize.Width - 20;
            }
        }

        private void templateEditorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            templateEditorToolStripMenuItem.Checked = !templateEditorToolStripMenuItem.Checked;
            groupTemplateEditor.Visible = templateEditorToolStripMenuItem.Checked;
            groupBoxSecondary.Visible = !templateEditorToolStripMenuItem.Checked;
            compareLogsToolStripMenuItem.Checked = !templateEditorToolStripMenuItem.Checked;
        }

        private void compareLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {

            compareLogsToolStripMenuItem.Checked = !compareLogsToolStripMenuItem.Checked;
            groupTemplateEditor.Visible = !compareLogsToolStripMenuItem.Checked;
            groupBoxSecondary.Visible = compareLogsToolStripMenuItem.Checked;
            templateEditorToolStripMenuItem.Checked = !compareLogsToolStripMenuItem.Checked;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            comboVersions.SelectedIndex = 2;
            comboVersions.Enabled = false;

            comboModel.SelectedIndex = 2;
            int a = listPairs.SelectedIndex; // Force the SelectedIndexChanged event to run and populate the dynamic register target combo box.
        }
    }
}
