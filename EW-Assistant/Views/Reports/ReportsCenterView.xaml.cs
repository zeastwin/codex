using System.Windows;
using System.Windows.Controls;
using EW_Assistant.ViewModels;

namespace EW_Assistant.Views.Reports
{
    /// <summary>
    /// 报表中心视图，暂时展示占位数据与示例 Markdown。
    /// </summary>
    public partial class ReportsCenterView : UserControl
    {
        private readonly ReportsCenterViewModel _viewModel;

        public ReportsCenterView()
        {
            InitializeComponent();
            _viewModel = new ReportsCenterViewModel();
            DataContext = _viewModel;
        }

        private void DailyProdButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType("当日产能报表");
        }

        private void WeeklyProdButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType("产能周报");
        }

        private void DailyAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType("当日报警报表");
        }

        private void WeeklyAlarmButton_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SwitchReportType("报警周报");
        }
    }
}
