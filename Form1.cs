using System.Text;
using System.Globalization;

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
            WireOptionalHeaderVisibilityCheckboxes();
            WirePaneSelectionHandlers();
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
                RefreshPanePreviewDisplay(pane);
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
            checkBox.CheckedChanged += (_, _) => RefreshPanePreviewDisplay(pane);
        }

        private void RefreshPanePreviewDisplay(LogPaneState pane)
        {
            SetListBoxItems(pane.ListBox, BuildPreviewDisplayLines(pane));
        }

        private static IReadOnlyList<string> BuildPreviewDisplayLines(LogPaneState pane)
        {
            if (pane.PreviewLines.Count == 0)
            {
                return Array.Empty<string>();
            }

            if (!pane.LogData.HasTemplateHeader)
            {
                return pane.PreviewLines;
            }

            List<string> displayLines = new(pane.PreviewLines.Count);
            bool hideHeaderLine = pane.HeaderVisibilityCheckBox?.Checked == true;

            // ZXTL first line is template and is never shown in the list.
            if (!hideHeaderLine && !string.IsNullOrEmpty(pane.LogData.HeaderLine))
            {
                displayLines.Add(pane.LogData.HeaderLine);
            }

            // Remaining trace preview starts after template (line 1) and header (line 2).
            for (int i = 2; i < pane.PreviewLines.Count; i++)
            {
                displayLines.Add(pane.PreviewLines[i]);
            }

            return displayLines;
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
            }
        }

        private static ushort? ComposePair(byte? high, byte? low)
        {
            if (high is null || low is null)
            {
                return null;
            }

            return (ushort)((high.Value << 8) | low.Value);
        }

        private static void SetRegisterTextBox(TextBox textBox, string text)
        {
            textBox.Text = text;
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
            RegisterValueParseMode registerParseMode = pane.LogData.Opts.Hex
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
                    registerParseMode = item.ValueFormat switch
                    {
                        TraceLogOrderValueFormat.Hex => RegisterValueParseMode.Hex,
                        TraceLogOrderValueFormat.Decimal => RegisterValueParseMode.Decimal,
                        _ => registerParseMode
                    };

                    continue;
                }

                if (item.Kind != TraceLogOrderItemKind.Register)
                {
                    continue;
                }

                if (TryAssignRegisterFromString(
                    pane.LogData.SelectedLine.Registers,
                    item.Field,
                    rawValue,
                    registerParseMode,
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
                    return TryAssignByte(v => registers.A = v, () => registers.A = null, numericValue, out error);
                case TraceLogOrderField.F:
                    return TryAssignByte(v => registers.F = v, () => registers.F = null, numericValue, out error);
                case TraceLogOrderField.B:
                    return TryAssignByte(v => registers.B = v, () => registers.B = null, numericValue, out error);
                case TraceLogOrderField.C:
                    return TryAssignByte(v => registers.C = v, () => registers.C = null, numericValue, out error);
                case TraceLogOrderField.D:
                    return TryAssignByte(v => registers.D = v, () => registers.D = null, numericValue, out error);
                case TraceLogOrderField.E:
                    return TryAssignByte(v => registers.E = v, () => registers.E = null, numericValue, out error);
                case TraceLogOrderField.H:
                    return TryAssignByte(v => registers.H = v, () => registers.H = null, numericValue, out error);
                case TraceLogOrderField.L:
                    return TryAssignByte(v => registers.L = v, () => registers.L = null, numericValue, out error);
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

        private static void SetRegisterFieldNull(TraceLogRegisters registers, TraceLogOrderField field)
        {
            switch (field)
            {
                case TraceLogOrderField.A: registers.A = null; break;
                case TraceLogOrderField.F: registers.F = null; break;
                case TraceLogOrderField.B: registers.B = null; break;
                case TraceLogOrderField.C: registers.C = null; break;
                case TraceLogOrderField.D: registers.D = null; break;
                case TraceLogOrderField.E: registers.E = null; break;
                case TraceLogOrderField.H: registers.H = null; break;
                case TraceLogOrderField.L: registers.L = null; break;
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
