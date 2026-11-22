using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace ArknightsRecruitTool;

public sealed class RecruitOverlay : Form
{
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 28, ForeColor = Color.White, BackColor = Color.FromArgb(35, 35, 35), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Panel _results = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(22, 22, 22), AutoScroll = true };
    private readonly Button _recruitSite = new() { Dock = DockStyle.Bottom, Height = 34, Text = "Open Krooster Recruit Tool", FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(0, 120, 212), ForeColor = Color.White };
    private Dictionary<string, string> _tagNames = new(StringComparer.OrdinalIgnoreCase);
    private List<Operator> _operators = [];

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public RecruitOverlay()
    {
        Text = "Arknights Recruitment";
        Size = new Size(680, 700);
        StartPosition = FormStartPosition.Manual;
        Location = new Point(Screen.PrimaryScreen!.WorkingArea.Width - Width - 20, 100);
        TopMost = true;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        Controls.Add(_results);
        Controls.Add(_status);
        Controls.Add(_recruitSite);
        _recruitSite.Click += (_, _) => Process.Start(new ProcessStartInfo("https://www.krooster.com/tools/recruit") { UseShellExecute = true });
        _status.Text = "Loading tag data...";
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        RegisterHotKey(Handle, 1, 0, 0x77); // F8
        _ = LoadDataAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        UnregisterHotKey(Handle, 1);
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && m.WParam.ToInt32() == 1)
            _ = CaptureAndScanAsync();
        base.WndProc(ref m);
    }

    private async Task LoadDataAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ArknightsRecruitTool/1.0");
            const string pageUrl = "https://www.krooster.com/tools/recruit";
            var page = await http.GetStringAsync(pageUrl);
            var bundleMatch = Regex.Match(page, @"src=[""'](?<src>[^""']*pages/tools/recruit-[^""']+\.js)[""']");
            if (!bundleMatch.Success)
                throw new InvalidDataException("Could not locate Krooster's recruitment data bundle.");

            var bundleUrl = new Uri(new Uri(pageUrl), bundleMatch.Groups["src"].Value);
            var bundle = await http.GetStringAsync(bundleUrl);
            var dataMatch = Regex.Match(bundle, @"JSON\.parse\('((?:\\.|[^'\\])*)'\)");
            if (!dataMatch.Success)
                throw new InvalidDataException("Could not find embedded recruitment data in Krooster's bundle.");

            var recruitmentData = JsonNode.Parse(DecodeJavaScriptString(dataMatch.Groups[1].Value))?.AsObject()
                ?? throw new InvalidDataException("Krooster returned invalid recruitment data.");
            var operatorsById = new Dictionary<string, Operator>(StringComparer.Ordinal);

            foreach (var group in recruitmentData)
            {
                var groupData = group.Value?.AsObject()
                    ?? throw new InvalidDataException($"Krooster returned an invalid group: {group.Key}.");
                var listedOperators = groupData["operators"]?.AsArray()
                    ?? throw new InvalidDataException($"Krooster group has no operator list: {group.Key}.");

                foreach (var opNode in listedOperators)
                {
                    var item = opNode?.AsObject()
                        ?? throw new InvalidDataException($"Krooster returned an invalid operator in {group.Key}.");
                    var id = item["id"]?.GetValue<string>()
                        ?? throw new InvalidDataException($"Operator in {group.Key} has no id.");
                    var name = item["name"]?.GetValue<string>()
                        ?? throw new InvalidDataException($"Operator {id} has no name.");
                    var level = item["rarity"]?.GetValue<int>()
                        ?? throw new InvalidDataException($"Operator {id} has no rarity.");
                    var tags = item["tags"]?.AsArray()
                        .Select(tag => tag?.GetValue<string>() ?? throw new InvalidDataException($"Operator {id} has an invalid tag."))
                        .ToHashSet(StringComparer.Ordinal)
                        ?? throw new InvalidDataException($"Operator {id} has no tags.");

                    foreach (var tag in tags)
                        _tagNames[tag] = tag;

                    if (operatorsById.TryGetValue(id, out var existing))
                    {
                        existing.Tags.UnionWith(tags);
                    }
                    else
                    {
                        operatorsById.Add(id, new Operator(name, level, tags));
                    }
                }
            }

            _operators = operatorsById.Values.ToList();
            _status.Text = $"Krooster: {_operators.Count} operators - F8 to capture";
        }
        catch (Exception ex)
        {
            _status.Text = "Data load failed: " + ex.Message;
        }
    }

    private static string DecodeJavaScriptString(string value)
    {
        var decoded = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\')
            {
                decoded.Append(value[i]);
                continue;
            }

            if (++i >= value.Length)
                throw new InvalidDataException("Krooster data contains an incomplete escape sequence.");

            var escape = value[i];
            switch (escape)
            {
                case '\'':
                case '"':
                case '\\':
                case '/':
                    decoded.Append(escape);
                    break;
                case 'b': decoded.Append('\b'); break;
                case 'f': decoded.Append('\f'); break;
                case 'n': decoded.Append('\n'); break;
                case 'r': decoded.Append('\r'); break;
                case 't': decoded.Append('\t'); break;
                case 'v': decoded.Append('\v'); break;
                case '0': decoded.Append('\0'); break;
                case 'u':
                    if (i + 4 >= value.Length || !ushort.TryParse(value.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var unicode))
                        throw new InvalidDataException("Krooster data contains an invalid Unicode escape.");
                    decoded.Append((char)unicode);
                    i += 4;
                    break;
                case 'x':
                    if (i + 2 >= value.Length || !byte.TryParse(value.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
                        throw new InvalidDataException("Krooster data contains an invalid hexadecimal escape.");
                    decoded.Append((char)hex);
                    i += 2;
                    break;
                default:
                    throw new InvalidDataException($"Krooster data contains an unsupported escape: \\{escape}.");
            }
        }

        return decoded.ToString();
    }

    private async Task CaptureAndScanAsync()
    {
        try
        {
            _results.Controls.Clear();
            _status.Text = "Scanning...";

            var hwnd = FindArknightsWindow();
            if (hwnd == nint.Zero)
            {
                _status.Text = "Arknights window not found";
                return;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                _status.Text = "Could not find Arknights window";
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap))
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height));

            var diagnosticDir = Path.Combine(Application.LocalUserAppDataPath, "ArknightsRecruitTool");
            Directory.CreateDirectory(diagnosticDir);
            bitmap.Save(Path.Combine(diagnosticDir, "last-capture.png"), ImageFormat.Png);

            var ocrText = await OcrBitmapAsync(bitmap);
            var recognized = RecognizeTags(ocrText);
            if (recognized.Count == 0)
            {
                RenderMessage(
                    "No recruitment tags recognized",
                    "OCR saw:" + Environment.NewLine + ocrText);
                _status.Text = $"Scan complete - {stopwatch.ElapsedMilliseconds} ms";
                return;
            }

            var recognizedNames = recognized
                .Select(tag => _tagNames.FirstOrDefault(kv => kv.Value == tag).Key)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();
            var combinations = GetCombinations(recognized.ToList(), 3);
            var unique = new Dictionary<string, Operator>(StringComparer.OrdinalIgnoreCase);
            var guaranteeCombinations = new Dictionary<string, Dictionary<string, bool>>(StringComparer.OrdinalIgnoreCase);

            var topOperatorVisible = recognized.Contains("Top Operator", StringComparer.OrdinalIgnoreCase);
            if (topOperatorVisible)
            {
                foreach (var combo in combinations.Where(tags => tags.Contains("Top Operator", StringComparer.OrdinalIgnoreCase)))
                {
                    var sixStarMatches = _operators
                        .Where(op => op.Level == 6 && combo.All(tag => op.Tags.Contains(tag)))
                        .ToList();

                    foreach (var op in sixStarMatches)
                    {
                        if (!unique.ContainsKey(op.Name))
                            unique[op.Name] = op;

                        if (!guaranteeCombinations.TryGetValue(op.Name, out var labels))
                        {
                            labels = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                            guaranteeCombinations[op.Name] = labels;
                        }

                        var englishCombo = combo
                            .Select(tag => _tagNames.FirstOrDefault(kv => kv.Value == tag).Key)
                            .Where(name => !string.IsNullOrEmpty(name));
                        labels[string.Join(" + ", englishCombo)] = false;
                    }
                }
            }

            foreach (var combo in combinations)
            {
                if (combo.Contains("Top Operator", StringComparer.OrdinalIgnoreCase))
                    continue;

                var matches = _operators
                    .Where(op => op.Level > 1 && combo.All(tag => op.Tags.Contains(tag)))
                    .ToList();
                if (matches.Count == 0)
                    continue;

                var guaranteedRarity = matches.Min(op => op.Level);
                if (guaranteedRarity < 4)
                    continue;

                foreach (var op in matches)
                {
                    if (op.Level != guaranteedRarity || op.Level >= 6)
                        continue;

                    if (!unique.ContainsKey(op.Name) || op.Level > unique[op.Name].Level)
                        unique[op.Name] = op;

                    if (!guaranteeCombinations.TryGetValue(op.Name, out var labels))
                    {
                        labels = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                        guaranteeCombinations[op.Name] = labels;
                    }

                    var englishCombo = combo
                        .Select(tag => _tagNames.FirstOrDefault(kv => kv.Value == tag).Key)
                        .Where(name => !string.IsNullOrEmpty(name));
                    var combinationLabel = string.Join(" + ", englishCombo);
                    labels[combinationLabel] = matches.Any(match => match.Level == 5);
                }
            }

            if (unique.Count == 0)
            {
                RenderMessage(
                    "Recognized tags: " + string.Join(", ", recognizedNames),
                    "No guaranteed 4★ or higher result.");
            }
            else
            {
                RenderResults(
                    "Recognized tags: " + string.Join(", ", recognizedNames) + Environment.NewLine +
                    (topOperatorVisible ? "Guaranteed 4★–6★" : "Guaranteed 4★–5★"),
                    unique.Values.OrderByDescending(x => x.Level).ThenBy(x => x.Name),
                    guaranteeCombinations);
            }

            _status.Text = $"Found {unique.Count} operators - {stopwatch.ElapsedMilliseconds} ms";
            Clipboard.SetText(string.Join("\n", recognized));
        }
        catch (Exception ex)
        {
            _status.Text = "Scan failed";
            RenderMessage("Scan failed", ex.Message);
        }
    }

    private void RenderMessage(string heading, string message)
    {
        _results.Controls.Clear();
        var headingLabel = new Label
        {
            Text = heading,
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 8, 8, 4),
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(32, 32, 32),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        var messageLabel = new Label
        {
            Text = message,
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10)
        };
        _results.Controls.Add(messageLabel);
        _results.Controls.Add(headingLabel);
    }

    private void RenderResults(
        string recognizedText,
        IEnumerable<Operator> operators,
        Dictionary<string, Dictionary<string, bool>> combinationsByOperator)
    {
        _results.SuspendLayout();
        _results.Controls.Clear();

        var heading = new Label
        {
            Text = recognizedText,
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(10, 8, 8, 4),
            ForeColor = Color.Gainsboro,
            BackColor = Color.FromArgb(32, 32, 32),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        _results.Controls.Add(heading);

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = Color.FromArgb(22, 22, 22),
            Padding = new Padding(8, 4, 8, 8)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        table.Controls.Add(CreateColumnHeader("OPERATOR"), 0, 0);
        table.Controls.Add(CreateColumnHeader("TAG COMBINATIONS"), 1, 0);
        table.RowCount = 1;

        var rowIndex = 1;
        foreach (var op in operators)
        {
            var hasFiveStarPossibility = combinationsByOperator.TryGetValue(op.Name, out var operatorCombos)
                && operatorCombos.Values.Any(possible => possible);
            var baseRowColor = rowIndex % 2 == 0 ? Color.FromArgb(35, 35, 35) : Color.FromArgb(28, 28, 28);
            var left = new Label
            {
                Text = $"{op.Level}★  {op.Name}",
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 6, 4, 4),
                ForeColor = Color.White,
                BackColor = hasFiveStarPossibility ? Color.FromArgb(112, 78, 42) : baseRowColor,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            };
            var tagCell = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(4),
                BackColor = baseRowColor
            };
            if (combinationsByOperator.TryGetValue(op.Name, out var combos))
                foreach (var combo in combos.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                    tagCell.Controls.Add(CreateCombinationPanel(combo.Key));

            var rowHeight = Math.Max(58, tagCell.PreferredSize.Height + 8);
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, rowHeight));
            table.Controls.Add(left, 0, rowIndex);
            table.Controls.Add(tagCell, 1, rowIndex);
            table.RowCount++;
            rowIndex++;
        }

        _results.Controls.Add(table);
        _results.Controls.SetChildIndex(table, 0);
        _results.Controls.SetChildIndex(heading, 1);
        _results.ResumeLayout(true);
    }

    private static Label CreateColumnHeader(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 4, 4),
            ForeColor = Color.FromArgb(170, 190, 210),
            Font = new Font("Segoe UI", 8, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static FlowLayoutPanel CreateCombinationPanel(string combo)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4, 3, 4, 3),
            Margin = new Padding(2, 3, 2, 5),
            BackColor = Color.FromArgb(38, 42, 48),
            BorderStyle = BorderStyle.FixedSingle
        };

        var colors = new[]
        {
            Color.FromArgb(27, 105, 117),
            Color.FromArgb(92, 75, 145),
            Color.FromArgb(145, 91, 44)
        };
        var tags = combo.Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tags.Length; i++)
        {
            if (i > 0)
                panel.Controls.Add(new Label
                {
                    Text = "+",
                    AutoSize = true,
                    ForeColor = Color.Silver,
                    Margin = new Padding(3, 5, 3, 0)
                });

            panel.Controls.Add(new Label
            {
                Text = tags[i],
                AutoSize = true,
                Padding = new Padding(6, 3, 6, 3),
                ForeColor = Color.White,
                BackColor = colors[i % colors.Length],
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Margin = new Padding(1, 1, 1, 1)
            });
        }

        return panel;
    }

    private static nint FindArknightsWindow()
    {
        foreach (var process in Process.GetProcessesByName("Arknights"))
            if (process.MainWindowHandle != nint.Zero)
                return process.MainWindowHandle;

        return FindWindow(null, "Arknights");
    }

    private async Task<string> OcrBitmapAsync(Bitmap bitmap)
    {
        var recognizer = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
        if (recognizer is null)
            throw new InvalidOperationException("The Windows English OCR language pack is not installed.");

        using var stream = new InMemoryRandomAccessStream();
        bitmap.Save(stream.AsStream(), ImageFormat.Bmp);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await recognizer.RecognizeAsync(softwareBitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(x => x.Text));
    }

    private HashSet<string> RecognizeTags(string text)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        var ocrLines = text.Split(
                [Environment.NewLine, "\n", "\r"],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeText)
            .ToHashSet(StringComparer.Ordinal);
        var hasSupporter = ocrLines.Contains("SUPPORTER");

        foreach (var pair in _tagNames)
        {
            var normalizedKey = NormalizeText(pair.Key);
            if (ocrLines.Contains(normalizedKey))
                found.Add(pair.Value);

            foreach (var alias in GetAliases(pair.Key))
                if (ocrLines.Contains(NormalizeText(alias)))
                    found.Add(pair.Value);
        }

        if (hasSupporter)
            found.Remove("Support");

        return found;
    }

    private static string NormalizeText(string value)
    {
        return new string(value
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static IEnumerable<string> GetAliases(string tag)
    {
        return tag.ToUpperInvariant() switch
        {
            "DP-RECOVERY" => ["DP RECOVERY", "DPRECOVERY", "DP RECOVER"],
            "FAST-REDEPLOY" => ["FAST REDEPLOY", "FAST REDEPLOYMENT", "FASTREDEPLOY"],
            "CROWD CONTROL" => ["CROWDCONTROL"],
            "DEFENDER" => ["DEFENCE", "DEFENSE"],
            "MELEE" => ["MELE"],
            "FEMALE" => ["FEMA1E"],
            _ => []
        };
    }

    private static IEnumerable<List<string>> GetCombinations(List<string> items, int maxCount)
    {
        for (var count = 1; count <= Math.Min(maxCount, items.Count); count++)
            foreach (var combo in Combine(items, count))
                yield return combo;
    }

    private static IEnumerable<List<string>> Combine(List<string> items, int count)
    {
        var selected = new string[count];
        IEnumerable<List<string>> Recurse(int start, int depth)
        {
            if (depth == count)
            {
                yield return new List<string>(selected);
                yield break;
            }
            for (var i = start; i <= items.Count - (count - depth); i++)
            {
                selected[depth] = items[i];
                foreach (var value in Recurse(i + 1, depth + 1))
                    yield return value;
            }
        }
        return Recurse(0, 0);
    }

    private sealed record Operator(string Name, int Level, HashSet<string> Tags);
}
