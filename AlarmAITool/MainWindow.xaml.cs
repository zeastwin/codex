using AlarmAITool.Services;
using ClosedXML.Excel;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace AlarmAITool
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private const string AppConfigPath = @"D:\AppConfig.json";
        private CancellationTokenSource _cts;
        private bool _isRunning;

        public ObservableCollection<string> StatusItems { get; } = new ObservableCollection<string>();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            AppendStatus("已就绪。");
            UpdateStartButtonState();
        }

        private void SelectInput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            InputPathBox.Text = dialog.FileName;
            OutputPathBox.Text = string.Empty;
            AppendStatus("已选择Excel文件，请设置保存路径。");
            UpdateStartButtonState();
        }

        private void SelectOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel 文件 (*.xlsx)|*.xlsx",
                FileName = BuildDefaultOutputName()
            };

            if (dialog.ShowDialog() != true)
                return;

            OutputPathBox.Text = dialog.FileName;
            AppendStatus("已设置保存路径。");
            UpdateStartButtonState();
        }

        private async void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            var inputPath = InputPathBox.Text?.Trim();
            var outputPath = OutputPathBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(outputPath))
            {
                AppendStatus("请先设置保存路径。");
                return;
            }

            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
            {
                AppendStatus("请先选择有效的Excel文件。");
                return;
            }

            if (IsSamePath(inputPath, outputPath))
            {
                AppendStatus("保存路径不能与原文件相同。");
                return;
            }

            if (!TryLoadConfig(out var baseUrl, out var autoKey, out var error))
            {
                AppendStatus(error);
                return;
            }

            var overwriteExisting = OverwriteCheck.IsChecked == true;
            SetRunningState(true);
            _cts = new CancellationTokenSource();

            try
            {
                await Task.Run(() => ProcessAsync(inputPath, outputPath, baseUrl, autoKey, overwriteExisting, _cts.Token));
                AppendStatus("处理完成。");
            }
            catch (OperationCanceledException)
            {
                AppendStatus("已取消。");
            }
            catch (Exception ex)
            {
                AppendStatus("执行异常：" + ex.Message);
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetRunningState(false);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private async Task ProcessAsync(string inputPath, string outputPath, string baseUrl, string autoKey, bool overwriteExisting, CancellationToken ct)
        {
            AppendStatus("开始加载Excel。");

            using var stream = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet == null)
            {
                AppendStatus("未找到工作表，已退出。");
                return;
            }

            var usedRange = worksheet.RangeUsed();
            if (usedRange == null)
            {
                AppendStatus("未找到有效数据，已退出。");
                return;
            }

            worksheet.Cell(1, 7).Value = "AI回答";

            var firstRow = Math.Max(2, usedRange.FirstRowUsed().RowNumber());
            var lastRow = usedRange.LastRowUsed().RowNumber();
            var totalRows = Math.Max(0, lastRow - firstRow + 1);

            var client = new DifyAutoClient(baseUrl, autoKey);

            int processed = 0;
            int skipped = 0;
            UpdateProgress(processed, totalRows, skipped);

            for (int row = firstRow; row <= lastRow; row++)
            {
                ct.ThrowIfCancellationRequested();

                var errorDesc = worksheet.Cell(row, 2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(errorDesc))
                {
                    skipped++;
                    AppendStatus($"第{row}行 B列为空，已跳过。");
                    UpdateProgress(processed, totalRows, skipped);
                    continue;
                }

                var existing = worksheet.Cell(row, 7).GetString().Trim();
                if (!overwriteExisting && !string.IsNullOrWhiteSpace(existing))
                {
                    skipped++;
                    AppendStatus($"第{row}行 G列已有值，已跳过。");
                    UpdateProgress(processed, totalRows, skipped);
                    continue;
                }

                if (overwriteExisting && !string.IsNullOrWhiteSpace(existing))
                    AppendStatus($"第{row}行 G列已有值，将覆盖。");

                var errorCode = worksheet.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(errorCode))
                    errorCode = "0";

                AppendStatus($"第{row}行 开始请求。");
                AppendStatus($"第{row}行 ErrorDesc：{errorDesc}");
                AlarmToolLogger.LogRequest(row, errorDesc);

                string result;
                try
                {
                    result = await client.RunAsync(errorDesc, errorCode, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AlarmToolLogger.LogError(row, ex.Message);
                    AppendStatus($"第{row}行 请求失败：{ex.Message}");
                    skipped++;
                    UpdateProgress(processed, totalRows, skipped);
                    continue;
                }

                worksheet.Cell(row, 7).Value = result ?? string.Empty;
                AlarmToolLogger.LogResponse(row, result ?? string.Empty);

                try
                {
                    SaveWorkbook(workbook, outputPath);
                }
                catch (Exception ex)
                {
                    AlarmToolLogger.LogError(row, "保存失败：" + ex.Message);
                    AppendStatus($"第{row}行 保存失败：{ex.Message}");
                    throw;
                }

                processed++;
                AppendStatus($"第{row}行 完成并保存。");
                UpdateProgress(processed, totalRows, skipped);
            }
        }

        private static void SaveWorkbook(XLWorkbook workbook, string outputPath)
        {
            workbook.SaveAs(outputPath);
        }

        private void UpdateStartButtonState()
        {
            StartButton.IsEnabled = CanStart();
        }

        private void SetRunningState(bool isRunning)
        {
            _isRunning = isRunning;
            Dispatcher.Invoke(() =>
            {
                StartButton.IsEnabled = CanStart();
                CancelButton.IsEnabled = isRunning;
            });
        }

        private void AppendStatus(string message)
        {
            var text = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Dispatcher.Invoke(() =>
            {
                if (StatusItems.Count >= 100)
                    StatusItems.Clear();
                StatusItems.Add(text);
                if (StatusItems.Count > 0)
                    StatusList.ScrollIntoView(StatusItems[StatusItems.Count - 1]);
            });
        }

        private void UpdateProgress(int processed, int total, int skipped)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressText.Text = $"已处理：{processed} / {total}，跳过：{skipped}";
                ProgressBar.Maximum = Math.Max(1, total);
                var current = Math.Min(total, processed + skipped);
                ProgressBar.Value = Math.Max(0, current);
            });
        }

        private static bool IsSamePath(string pathA, string pathB)
        {
            return string.Equals(Path.GetFullPath(pathA), Path.GetFullPath(pathB), StringComparison.OrdinalIgnoreCase);
        }

        private bool CanStart()
        {
            return !_isRunning
                   && !string.IsNullOrWhiteSpace(InputPathBox.Text)
                   && !string.IsNullOrWhiteSpace(OutputPathBox.Text);
        }

        private string BuildDefaultOutputName()
        {
            var input = InputPathBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(input))
                return "报警结果_AI.xlsx";

            var dir = Path.GetDirectoryName(input);
            var name = Path.GetFileNameWithoutExtension(input);
            return Path.Combine(dir ?? string.Empty, name + "_AI.xlsx");
        }

        private static bool TryLoadConfig(out string baseUrl, out string autoKey, out string error)
        {
            baseUrl = null;
            autoKey = null;
            error = null;

            if (!File.Exists(AppConfigPath))
            {
                error = "未找到 D:\\AppConfig.json，无法读取 URL/AutoKey。";
                return false;
            }

            try
            {
                var json = File.ReadAllText(AppConfigPath, Encoding.UTF8);
                var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var dict = serializer.DeserializeObject(json) as System.Collections.Generic.Dictionary<string, object>;
                if (dict == null)
                {
                    error = "配置文件解析失败。";
                    return false;
                }

                baseUrl = GetString(dict, "URL")?.Trim().TrimEnd('/');
                autoKey = GetString(dict, "AutoKey")?.Trim();

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(autoKey))
                {
                    error = "URL/AutoKey 未配置。";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "读取配置失败：" + ex.Message;
                return false;
            }
        }

        private static string GetString(System.Collections.Generic.Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.TryGetValue(key, out var value))
                return value?.ToString();
            return null;
        }
    }
}
