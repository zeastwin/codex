using EW_Assistant.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace EW_Assistant.Views
{
    public partial class ProductionBoardView : UserControl
    {
        public static readonly DependencyProperty FilePrefixProperty =
            DependencyProperty.Register(nameof(FilePrefix), typeof(string), typeof(ProductionBoardView),
                new PropertyMetadata("小时产量", OnFilePrefixChanged));

        public string FilePrefix
        {
            get => (string)GetValue(FilePrefixProperty);
            set => SetValue(FilePrefixProperty, value);
        }

        private const double Stagger = 0.08;
        private const double LowYieldThreshold = 0.95;

        private static readonly SKTypeface FONT_CJK = ResolveCjkTypeface();
        private readonly DispatcherTimer _autoTimer;
        private readonly bool _autoRefreshEnabled = true;
        private readonly Stopwatch _sw = new();
        private readonly TimeSpan _animDuration = TimeSpan.FromMilliseconds(520);

        private bool _isAnimating;
        private bool _isReloading;
        private double _anim = 1.0;
        private DateTime _day = DateTime.Today;
        private string? _dayFile;
        private bool _dayMissing;

        private readonly List<DayData> _week = new();
        private readonly int[] _hourPass = new int[24];
        private readonly int[] _hourFail = new int[24];
        private int _sumPass;
        private int _sumFail;

        private readonly ObservableCollection<HourRow> _hourRows = new();
        private readonly ObservableCollection<TopHour> _topRows = new();

        public ProductionBoardView()
        {
            InitializeComponent();

            TopList.ItemsSource = _topRows;

            DayPicker.SelectedDate = _day;
            TitleDay.Text = FormatDay(_day);

            Loaded += ProductionBoardView_Loaded;
            Unloaded += ProductionBoardView_Unloaded;

            ConfigService.ConfigChanged += ConfigService_ConfigChanged;

            _autoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _autoTimer.Tick += AutoTimer_Tick;

            _ = Dispatcher.InvokeAsync(() => ReloadAll(animate: false, reloadWeek: true));
        }

        private static void OnFilePrefixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ProductionBoardView view && view.IsLoaded)
            {
                view.ReloadAll(animate: false, reloadWeek: true);
            }
        }

        private void ProductionBoardView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_autoRefreshEnabled && !_autoTimer.IsEnabled)
                _autoTimer.Start();

            ReloadAll(animate: true, reloadWeek: true);
        }

        private void ProductionBoardView_Unloaded(object sender, RoutedEventArgs e)
        {
            _autoTimer.Stop();
            CompositionTarget.Rendering -= OnRendering;
        }

        private void ConfigService_ConfigChanged(object? sender, Settings.AppConfig e)
        {
            Dispatcher.Invoke(() => ReloadAll(animate: false, reloadWeek: true));
        }

        private void AutoTimer_Tick(object? sender, EventArgs e)
        {
            if (!ShouldAutoRefresh()) return;
            ReloadAll(animate: false, reloadWeek: false);
        }

        private bool ShouldAutoRefresh() => _autoRefreshEnabled && _day.Date == DateTime.Today;

        private void DayPicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DayPicker.SelectedDate is not DateTime dt) return;
            _day = dt.Date;
            TitleDay.Text = FormatDay(_day);
            ReloadAll(animate: false, reloadWeek: true);
        }

        private void BtnToday_Click(object sender, RoutedEventArgs e)
        {
            DayPicker.SelectedDate = DateTime.Today;
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ReloadAll(animate: false, reloadWeek: true);
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var root = ConfigService.Current.ProductionLogPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                MainWindow.PostProgramInfo("产能路径未配置，无法打开目录。", "warn");
                return;
            }
            try
            {
                if (!Directory.Exists(root))
                    Directory.CreateDirectory(root);

                using var proc = Process.Start(new ProcessStartInfo("explorer.exe", root)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo($"打开目录失败：{ex.Message}", "error");
            }
        }

        private void ReloadAll(bool animate, bool reloadWeek)
        {
            if (_isReloading) return;
            _isReloading = true;

            try
            {
                if (reloadWeek)
                    ReloadWeek();

                ReloadDayHours();
                UpdateBindings();
                UpdateHighlights();
                UpdateWarnings();

                if (animate)
                    StartAnimation();
                else
                {
                    _anim = 1.0;
                    SkWeek?.InvalidateVisual();
                    SkHour?.InvalidateVisual();
                }
            }
            catch (Exception ex)
            {
                MainWindow.PostProgramInfo($"产能看板刷新失败：{ex.Message}", "warn");
            }
            finally
            {
                _isReloading = false;
            }
        }

        private void ReloadWeek()
        {
            _week.Clear();
            for (int i = 6; i >= 0; i--)
            {
                var day = _day.AddDays(-i);
                var item = new DayData { Date = day, Missing = true };

                if (TryResolveCsv(day, out var file))
                {
                    try
                    {
                        var (pass, fail) = SumPassFailShared(file);
                        item.Pass = pass;
                        item.Fail = fail;
                        item.Missing = false;
                    }
                    catch
                    {
                        item.Missing = true;
                    }
                }

                _week.Add(item);
            }

            SkWeek?.InvalidateVisual();
        }

        private void ReloadDayHours()
        {
            Array.Clear(_hourPass, 0, _hourPass.Length);
            Array.Clear(_hourFail, 0, _hourFail.Length);
            _sumPass = _sumFail = 0;
            _dayMissing = false;
            _dayFile = null;

            if (!TryResolveCsv(_day, out var file))
            {
                _dayMissing = true;
                return;
            }

            _dayFile = file;

            Encoding enc;
            try { enc = Encoding.GetEncoding("GB2312"); }
            catch { enc = new UTF8Encoding(false); }

            List<string> lines;
            try
            {
                lines = ReadAllLinesShared(file, enc);
            }
            catch
            {
                _dayMissing = true;
                return;
            }

            if (lines.Count == 0)
            {
                _dayMissing = true;
                return;
            }

            var header = SmartSplit(lines[0]);
            int idxPass = FindIndex(header, "PASS", "良品", "良率PASS", "OK");
            int idxFail = FindIndex(header, "FAIL", "不良", "NG");
            int idxHour = FindIndex(header, "HOUR", "小时", "时段", "时刻");
            bool hasHour = idxHour >= 0;

            for (int i = 1; i < lines.Count; i++)
            {
                var row = SmartSplit(lines[i]);
                if (row.Length == 0) continue;

                int hour;
                if (hasHour)
                {
                    if (idxHour >= row.Length) continue;
                    hour = ExtractHour(row[idxHour]);
                }
                else
                {
                    hour = i - 1;
                }
                if (hour < 0 || hour > 23) continue;

                int pass = (idxPass >= 0 && idxPass < row.Length) ? ToInt(row[idxPass]) : 0;
                int fail = (idxFail >= 0 && idxFail < row.Length) ? ToInt(row[idxFail]) : 0;

                _hourPass[hour] += Math.Max(0, pass);
                _hourFail[hour] += Math.Max(0, fail);
            }

            _sumPass = _hourPass.Sum();
            _sumFail = _hourFail.Sum();

            _hourRows.Clear();
            for (int h = 0; h < 24; h++)
            {
                _hourRows.Add(new HourRow
                {
                    Hour = $"{h:00}:00 - {(h + 1) % 24:00}:00",
                    Pass = _hourPass[h],
                    Fail = _hourFail[h]
                });
            }

            TitleHour.Text = $"{_day:yyyy-MM-dd} · 24 小时产出";
            SkHour?.InvalidateVisual();
        }

        private void UpdateBindings()
        {
            var total = _sumPass + _sumFail;
            KpiTotal.Text = FormatNumber(total);
            KpiPass.Text = FormatNumber(_sumPass);
            KpiFail.Text = FormatNumber(_sumFail);
            KpiYield.Text = total > 0 ? $"{(_sumPass * 100.0 / total):0.0}%" : "—";

            var prev = _week.FirstOrDefault(d => d.Date == _day.AddDays(-1) && !d.Missing);
            if (prev != null && prev.Total > 0)
            {
                double diff = (total - prev.Total) / (double)prev.Total;
                KpiDelta.Text = $"{(diff >= 0 ? "+" : "")}{diff:P1} vs 昨日";
            }
            else
            {
                KpiDelta.Text = "暂无昨日对比数据";
            }

            var best = _hourRows.Where(r => r.Total > 0).OrderByDescending(r => r.Total).FirstOrDefault();
            var low = _hourRows.Where(r => r.Total > 0).OrderBy(r => r.Total).FirstOrDefault();

            PeakHourText.Text = best != null
                ? $"{best.Hour} · {best.Total:N0} 件"
                : "—";

            LowHourText.Text = low != null
                ? $"{low.Hour} · {low.Total:N0} 件"
                : "—";
        }

        private void UpdateHighlights()
        {
            _topRows.Clear();

            var data = _hourRows
                .Where(r => r.Total > 0)
                .OrderByDescending(r => r.Total)
                .Take(5)
                .ToList();

            int rank = 1;
            foreach (var row in data)
            {
                _topRows.Add(new TopHour
                {
                    Hour = row.Hour,
                    Total = row.Total,
                    TotalFormatted = FormatNumber(row.Total),
                    YieldText = row.YieldText,
                    Badge = $"TOP {rank}",
                    YieldBrush = row.Yield >= LowYieldThreshold ? Brushes.SeaGreen : Brushes.OrangeRed
                });
                rank++;
            }

            if (_topRows.Count == 0)
            {
                _topRows.Add(new TopHour
                {
                    Hour = "暂无数据",
                    Total = 0,
                    TotalFormatted = "0",
                    YieldText = "—",
                    Badge = "",
                    YieldBrush = Brushes.Gray
                });
            }
        }

        private void UpdateWarnings()
        {
            var root = ConfigService.Current.ProductionLogPath;
            if (string.IsNullOrWhiteSpace(root))
            {
                WarningHint.Text = "Config：未填写产能路径，无法读取 CSV";
                WarningHint.Visibility = Visibility.Visible;
                return;
            }

            if (_dayFile == null || _dayMissing)
            {
                var expected = Path.Combine(root, $"{FilePrefix}{_day:yyyyMMdd}.csv");
                WarningHint.Text = $"未找到 {_day:yyyy-MM-dd} · {Path.GetFileName(expected)}";
                WarningHint.Visibility = Visibility.Visible;
            }
            else
            {
                WarningHint.Visibility = Visibility.Collapsed;
            }
        }

        private string BuildClipboardSummary()
        {
            if (_dayMissing) return string.Empty;

            var total = _sumPass + _sumFail;
            var activeHours = _hourRows.Where(r => r.Total > 0).ToList();
            if (total <= 0 && activeHours.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"# {_day:yyyy-MM-dd} 产能摘要");
            sb.AppendLine($"- 总产量：{FormatNumber(total)}（PASS {FormatNumber(_sumPass)} / FAIL {FormatNumber(_sumFail)}）");

            if (total > 0)
                sb.AppendLine($"- 良率：{_sumPass * 100.0 / total:0.0}%");
            else
                sb.AppendLine("- 良率：—（暂无产出）");

            var peakHours = activeHours
                .OrderByDescending(r => r.Total)
                .ThenBy(r => r.Hour)
                .Take(3)
                .ToList();

            if (peakHours.Count > 0)
            {
                sb.AppendLine("- 峰值小时：");
                foreach (var row in peakHours)
                    sb.AppendLine($"  • {row.Hour} · {FormatNumber(row.Total)} 件 · 良率 {row.YieldText}");
            }

            var lowYieldHours = activeHours
                .Where(r => r.Yield < LowYieldThreshold)
                .OrderBy(r => r.Yield)
                .ThenBy(r => r.Hour)
                .Take(3)
                .ToList();

            if (lowYieldHours.Count > 0)
            {
                sb.AppendLine($"- 低良率小时（低于 {LowYieldThreshold:P0}）：");
                foreach (var row in lowYieldHours)
                    sb.AppendLine($"  • {row.Hour} · 良率 {row.YieldText} · 产量 {FormatNumber(row.Total)}");
            }

            int idleHours = 24 - activeHours.Count;
            sb.AppendLine($"- 有产出小时：{activeHours.Count}/24（停机 {idleHours}h）");

            return sb.ToString();
        }

        private void StartAnimation()
        {
            _sw.Restart();
            _anim = 0.0;
            _isAnimating = true;
            CompositionTarget.Rendering -= OnRendering;
            CompositionTarget.Rendering += OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (!_isAnimating) return;

            double t = _sw.Elapsed.TotalMilliseconds / _animDuration.TotalMilliseconds;
            _anim = Clamp01(t);

            SkWeek?.InvalidateVisual();
            SkHour?.InvalidateVisual();

            if (_anim >= 1.0)
            {
                _isAnimating = false;
                CompositionTarget.Rendering -= OnRendering;
                _anim = 1.0;
            }
        }

        private void SkWeek_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(SKColors.White);

            float L = 56, R = 24, T = 8, B = 46;
            var chart = new SKRect(L, T, info.Width - R, info.Height - B);

            using var gridPaint = new SKPaint { Color = new SKColor(240, 243, 248), StrokeWidth = 1, IsStroke = true };
            using var axisPaint = new SKPaint { Color = new SKColor(228, 232, 240), StrokeWidth = 1, IsStroke = true };
            using var textPaint = new SKPaint { Color = new SKColor(107, 114, 128), TextSize = 12, IsAntialias = true, Typeface = FONT_CJK };
            using var valuePaint = new SKPaint { IsAntialias = true, Color = new SKColor(55, 65, 81), TextSize = 12, Typeface = FONT_CJK };
            using var dayPaint = new SKPaint { IsAntialias = true, Color = new SKColor(107, 114, 128), TextSize = 12, Typeface = FONT_CJK };
            using var barPaint = new SKPaint { IsAntialias = true };

            for (int i = 0; i <= 4; i++)
            {
                float y = chart.Top + chart.Height * i / 4f;
                canvas.DrawLine(chart.Left, y, chart.Right, y, gridPaint);
            }
            canvas.DrawLine(chart.Left, chart.Bottom, chart.Right, chart.Bottom, axisPaint);
            canvas.DrawLine(chart.Left, chart.Top, chart.Left, chart.Bottom, axisPaint);

            if (_week.Count == 0)
                return;

            int max = Math.Max(1, _week.Where(w => !w.Missing).Select(w => w.Total).DefaultIfEmpty(1).Max());
            int yMax = (int)Math.Ceiling(max * 1.1);
            if (yMax <= 0) yMax = 10;

            for (int i = 0; i <= 4; i++)
            {
                int val = yMax * (4 - i) / 4;
                float y = chart.Top + chart.Height * i / 4f;
                var label = val.ToString(CultureInfo.InvariantCulture);
                var bounds = new SKRect();
                textPaint.MeasureText(label, ref bounds);
                canvas.DrawText(label, chart.Left - 8 - bounds.Width, y + bounds.Height / 2, textPaint);
            }

            float slot = chart.Width / _week.Count;
            float barWidth = slot * 0.52f;
            float radius = 6f;

            var passDark = new SKColor(59, 130, 246);
            var passLight = new SKColor(147, 197, 253);
            var failDark = new SKColor(239, 68, 68);
            var failLight = new SKColor(254, 202, 202);

            for (int i = 0; i < _week.Count; i++)
            {
                var d = _week[i];
                float cx = chart.Left + slot * i + slot / 2f;

                string dayLabel = d.Date.ToString("MM-dd", CultureInfo.InvariantCulture);
                var db = new SKRect();
                dayPaint.MeasureText(dayLabel, ref db);
                canvas.DrawText(dayLabel, cx - db.MidX, chart.Bottom + 18, dayPaint);

                double local = Clamp01((_anim - i * Stagger) / (1.0 - Math.Min(1.0, i * Stagger)));
                double eased = EaseOutCubic(local);

                if (d.Missing)
                {
                    valuePaint.Color = new SKColor(148, 163, 184);
                    var tb = new SKRect();
                    valuePaint.MeasureText("0", ref tb);
                    canvas.DrawText("0", cx - tb.MidX, chart.Bottom - 6, valuePaint);
                    continue;
                }

                float pH = chart.Height * (float)(d.Pass / Math.Max(1.0, yMax)) * (float)eased;
                float fH = chart.Height * (float)(d.Fail / Math.Max(1.0, yMax)) * (float)eased;
                float totalH = pH + fH;

                float left = cx - barWidth / 2f;
                float right = cx + barWidth / 2f;
                float bottom = chart.Bottom;

                if (pH > 0.1f)
                {
                    var rect = new SKRect(left, bottom - pH, right, bottom);
                    using var rr = MakeRRect(rect, 0, 0, radius, radius);
                    using var shader = SKShader.CreateLinearGradient(new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                        new[] { passLight, passDark }, null, SKShaderTileMode.Clamp);
                    barPaint.Shader = shader;
                    canvas.DrawRoundRect(rr, barPaint);
                    barPaint.Shader = null;
                }

                if (fH > 0.1f)
                {
                    var rect = new SKRect(left, bottom - pH - fH, right, bottom - pH);
                    using var rr = MakeRRect(rect, radius, radius, 0, 0);
                    using var shader = SKShader.CreateLinearGradient(new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                        new[] { failLight, failDark }, null, SKShaderTileMode.Clamp);
                    barPaint.Shader = shader;
                    canvas.DrawRoundRect(rr, barPaint);
                    barPaint.Shader = null;
                }

                var tag = (d.Pass + d.Fail).ToString(CultureInfo.InvariantCulture);
                var bounds = new SKRect();
                valuePaint.Color = new SKColor(55, 65, 81);
                valuePaint.MeasureText(tag, ref bounds);
                float desired = chart.Bottom - totalH - 6;
                float baseline = SafeBaseline(valuePaint, tag, desired, chart.Top, chart.Bottom);
                canvas.DrawText(tag, cx - bounds.MidX, baseline, valuePaint);
            }
        }

        private void SkHour_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            canvas.Clear(SKColors.White);

            float L = 56, R = 24, T = 8, B = 46;
            var chart = new SKRect(L, T, info.Width - R, info.Height - B);

            using var gridPaint = new SKPaint { Color = new SKColor(240, 243, 248), StrokeWidth = 1, IsStroke = true };
            using var axisPaint = new SKPaint { Color = new SKColor(228, 232, 240), StrokeWidth = 1, IsStroke = true };
            using var textPaint = new SKPaint { Color = new SKColor(107, 114, 128), TextSize = 12, IsAntialias = true, Typeface = FONT_CJK };
            using var valuePaint = new SKPaint { IsAntialias = true, Color = new SKColor(55, 65, 81), TextSize = 11, Typeface = FONT_CJK };
            using var barPaint = new SKPaint { IsAntialias = true };

            for (int i = 0; i <= 4; i++)
            {
                float y = chart.Top + chart.Height * i / 4f;
                canvas.DrawLine(chart.Left, y, chart.Right, y, gridPaint);
            }
            canvas.DrawLine(chart.Left, chart.Bottom, chart.Right, chart.Bottom, axisPaint);
            canvas.DrawLine(chart.Left, chart.Top, chart.Left, chart.Bottom, axisPaint);

            if (_dayMissing)
            {
                var msg = "缺少数据";
                var bounds = new SKRect();
                textPaint.MeasureText(msg, ref bounds);
                canvas.DrawText(msg, chart.MidX - bounds.MidX, chart.MidY - bounds.MidY, textPaint);
                return;
            }

            int max = 1;
            for (int i = 0; i < 24; i++)
                max = Math.Max(max, _hourPass[i] + _hourFail[i]);
            int yMax = (int)Math.Ceiling(max * 1.10);
            if (yMax <= 0) yMax = 10;

            for (int i = 0; i <= 4; i++)
            {
                int val = yMax * (4 - i) / 4;
                float y = chart.Top + chart.Height * i / 4f;
                var label = val.ToString(CultureInfo.InvariantCulture);
                var bounds = new SKRect();
                textPaint.MeasureText(label, ref bounds);
                canvas.DrawText(label, chart.Left - 8 - bounds.Width, y + bounds.Height / 2, textPaint);
            }

            float slot = chart.Width / 24f;
            float barWidth = slot * 0.62f;
            float radius = 5f;

            var passDark = new SKColor(59, 130, 246);
            var passLight = new SKColor(147, 197, 253);
            var failDark = new SKColor(239, 68, 68);
            var failLight = new SKColor(254, 202, 202);

            for (int i = 0; i < 24; i++)
            {
                int p = _hourPass[i];
                int f = _hourFail[i];
                int t = p + f;

                double local = Clamp01((_anim - i * 0.02) / (1.0 - Math.Min(1.0, i * 0.02)));
                double eased = EaseOutCubic(local);

                float pH = chart.Height * (float)(p / Math.Max(1.0, yMax)) * (float)eased;
                float fH = chart.Height * (float)(f / Math.Max(1.0, yMax)) * (float)eased;
                float totalH = pH + fH;

                float cx = chart.Left + slot * i + slot / 2f;
                float left = cx - barWidth / 2f;
                float right = cx + barWidth / 2f;
                float bottom = chart.Bottom;

                if (pH > 0.1f)
                {
                    var rect = new SKRect(left, bottom - pH, right, bottom);
                    using var rr = MakeRRect(rect, 0, 0, radius, radius);
                    using var shader = SKShader.CreateLinearGradient(new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                        new[] { passLight, passDark }, null, SKShaderTileMode.Clamp);
                    barPaint.Shader = shader;
                    canvas.DrawRoundRect(rr, barPaint);
                    barPaint.Shader = null;
                }

                if (fH > 0.1f)
                {
                    var rect = new SKRect(left, bottom - pH - fH, right, bottom - pH);
                    using var rr = MakeRRect(rect, radius, radius, 0, 0);
                    using var shader = SKShader.CreateLinearGradient(new SKPoint(0, rect.Top), new SKPoint(0, rect.Bottom),
                        new[] { failLight, failDark }, null, SKShaderTileMode.Clamp);
                    barPaint.Shader = shader;
                    canvas.DrawRoundRect(rr, barPaint);
                    barPaint.Shader = null;
                }

                if (t <= 0) continue;

                var tag = t.ToString(CultureInfo.InvariantCulture);
                var bounds = new SKRect();
                valuePaint.MeasureText(tag, ref bounds);
                float desired = chart.Bottom - totalH - 6;
                float baseline = SafeBaseline(valuePaint, tag, desired, chart.Top, chart.Bottom);
                canvas.DrawText(tag, cx - bounds.MidX, baseline, valuePaint);
            }
        }

        private bool TryResolveCsv(DateTime day, out string path)
        {
            path = string.Empty;
            var root = ConfigService.Current.ProductionLogPath;
            if (string.IsNullOrWhiteSpace(root)) return false;

            var name = $"{FilePrefix}{day:yyyyMMdd}.csv";
            var file = Path.Combine(root, name);
            if (File.Exists(file))
            {
                path = file;
                return true;
            }

            if (!Directory.Exists(root)) return false;

            var fallback = Directory.GetFiles(root, $"{FilePrefix}{day:yyyyMMdd}*.csv").FirstOrDefault();
            if (fallback != null)
            {
                path = fallback;
                return true;
            }
            return false;
        }

        private static List<string> ReadAllLinesShared(string path, Encoding enc)
        {
            var result = new List<string>();
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs, enc, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    result.Add(line);
            }
            return result;
        }

        private static (int pass, int fail) SumPassFailShared(string file)
        {
            Encoding enc;
            try { enc = Encoding.GetEncoding("GB2312"); }
            catch { enc = new UTF8Encoding(false); }

            var lines = ReadAllLinesShared(file, enc);
            if (lines.Count == 0) return (0, 0);

            var header = SmartSplit(lines[0]);
            int idxPass = FindIndex(header, "PASS", "良品");
            int idxFail = FindIndex(header, "FAIL", "不良");
            int idxHour = FindIndex(header, "HOUR", "小时", "时段", "时刻", "时间", "时别");
            bool hasHour = idxHour >= 0;

            int sumPass = 0, sumFail = 0;
            for (int i = 1; i < lines.Count; i++)
            {
                var row = SmartSplit(lines[i]);
                if (row.Length == 0) continue;

                int hour;
                if (hasHour)
                {
                    if (idxHour >= row.Length) continue;
                    hour = ExtractHour(row[idxHour]);
                }
                else
                {
                    hour = i - 1;
                }
                if (hour < 0 || hour > 23) continue;

                if (idxPass >= 0 && idxPass < row.Length) sumPass += Math.Max(0, ToInt(row[idxPass]));
                if (idxFail >= 0 && idxFail < row.Length) sumFail += Math.Max(0, ToInt(row[idxFail]));
            }
            return (sumPass, sumFail);
        }

        private static string[] SmartSplit(string line)
        {
            if (line.Contains(",")) return line.Split(',');
            if (line.Contains("\t")) return line.Split('\t');
            if (line.Contains(";")) return line.Split(';');
            return new[] { line };
        }

        private static int FindIndex(string[] arr, params string[] keys)
        {
            for (int i = 0; i < arr.Length; i++)
            {
                var t = arr[i].Trim().ToUpperInvariant();
                foreach (var k in keys)
                    if (t == k.Trim().ToUpperInvariant()) return i;
            }
            return -1;
        }

        private static int ExtractHour(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return -1;
            s = s.Trim();
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) && h >= 0 && h <= 23) return h;
            if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt)) return dt.Hour;
            if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt.Hour;
            var digits = new string(s.TakeWhile(char.IsDigit).ToArray());
            if (digits.Length > 0 && int.TryParse(digits, out h) && h >= 0 && h <= 23) return h;
            return -1;
        }

        private static int ToInt(string s)
        {
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return 0;
        }

        private static string FormatNumber(int value)
            => value.ToString("N0", CultureInfo.CurrentCulture);

        private static string FormatDay(DateTime day)
            => day.ToString("yyyy-MM-dd ddd", CultureInfo.CurrentCulture);

        private static SKRoundRect MakeRRect(SKRect rect, float tl, float tr, float br, float bl)
        {
            var rr = new SKRoundRect();
            rr.SetRectRadii(rect, new[]
            {
                new SKPoint(tl, tl), new SKPoint(tr, tr),
                new SKPoint(br, br), new SKPoint(bl, bl),
            });
            return rr;
        }

        private static float SafeBaseline(SKPaint paint, string text,
                                          float desiredBaseline,
                                          float chartTop, float chartBottom,
                                          float padTop = 10f, float padBottom = 4f)
        {
            var bounds = new SKRect();
            paint.MeasureText(text, ref bounds);
            float minBaseline = chartTop + padTop - bounds.Top;
            float maxBaseline = chartBottom - padBottom;
            if (desiredBaseline < minBaseline) return minBaseline;
            if (desiredBaseline > maxBaseline) return maxBaseline;
            return desiredBaseline;
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        private static double EaseOutCubic(double x) => 1 - Math.Pow(1 - x, 3);

        private static SKTypeface ResolveCjkTypeface()
        {
            string[] cands = { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "SimSun", "Noto Sans CJK SC", "Noto Sans SC", "PingFang SC" };
            foreach (var n in cands)
            {
                var tf = SKTypeface.FromFamilyName(n);
                if (tf != null)
                {
                    using var p = new SKPaint { Typeface = tf, TextSize = 12, IsAntialias = true };
                    if (p.ContainsGlyphs("产能数据123")) return tf;
                }
            }
            return SKTypeface.Default;
        }

        private sealed class DayData
        {
            public DateTime Date;
            public int Pass;
            public int Fail;
            public bool Missing;
            public int Total => Pass + Fail;
        }

        private sealed class HourRow
        {
            public string Hour { get; set; } = "";
            public int Pass { get; set; }
            public int Fail { get; set; }
            public int Total => Pass + Fail;
            public double Yield => Total > 0 ? (double)Pass / Total : 0;
            public string YieldText => Total > 0 ? $"{Yield * 100:0.0}%" : "—";
        }

        private sealed class TopHour
        {
            public string Hour { get; set; } = "";
            public int Total { get; set; }
            public string TotalFormatted { get; set; } = "0";
            public string YieldText { get; set; } = "—";
            public string Badge { get; set; } = "";
            public Brush YieldBrush { get; set; } = Brushes.Gray;
        }
    }
}
