using System;
using System.Windows;
using System.Windows.Input;

namespace EW_Assistant.Views.Inventory
{
    public class StockOperationResult
    {
        public int Quantity { get; set; }
        public int NewQuantity { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// 库存操作窗口（入库/出库/调整），一次性填写所有字段。
    /// </summary>
    public partial class StockOperationDialog : Window
    {
        private readonly string _mode;

        public StockOperationResult Result { get; private set; }

        public StockOperationDialog(string mode, string title, string subtitle, int defaultQty = 0, int defaultNewQty = 0)
        {
            InitializeComponent();
            _mode = mode ?? "Stock";

            TitleText.Text = string.IsNullOrWhiteSpace(title) ? "库存操作" : title;
            SubtitleText.Text = subtitle ?? string.Empty;

            QtyBox.Text = defaultQty > 0 ? defaultQty.ToString() : string.Empty;
            NewQtyBox.Text = defaultNewQty > 0 ? defaultNewQty.ToString() : string.Empty;
            ReasonBox.Text = string.Empty;

            // 调整模式显示“调整后库存”，入/出库隐藏
            var isAdjust = string.Equals(_mode, "Adjust", StringComparison.OrdinalIgnoreCase);
            NewQtyLabel.Visibility = isAdjust ? Visibility.Visible : Visibility.Collapsed;
            NewQtyBox.Visibility = isAdjust ? Visibility.Visible : Visibility.Collapsed;
            QtyLabel.Text = isAdjust ? "变更数量" : "数量";
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try { DragMove(); } catch { }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParseInt(QtyBox.Text, out var qty) && _mode != "Adjust")
            {
                MessageBox.Show("请输入有效数量。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var isAdjust = string.Equals(_mode, "Adjust", StringComparison.OrdinalIgnoreCase);
            int newQty = 0;
            if (isAdjust && !TryParseInt(NewQtyBox.Text, out newQty))
            {
                MessageBox.Show("请输入有效的调整后库存。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Result = new StockOperationResult
            {
                Quantity = qty,
                NewQuantity = newQty,
                Reason = ReasonBox.Text ?? string.Empty
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private bool TryParseInt(string text, out int value)
        {
            return int.TryParse(text, out value);
        }

        public static StockOperationResult Show(Window owner, string mode, string title, string subtitle, int defaultQty = 0, int defaultNewQty = 0)
        {
            var dlg = new StockOperationDialog(mode, title, subtitle, defaultQty, defaultNewQty)
            {
                Owner = owner
            };
            var ok = dlg.ShowDialog();
            return ok == true ? dlg.Result : null;
        }
    }
}
