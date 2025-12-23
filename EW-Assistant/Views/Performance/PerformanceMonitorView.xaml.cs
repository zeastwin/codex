using EW_Assistant.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace EW_Assistant.Views
{
    public partial class PerformanceMonitorView : UserControl
    {
        private readonly PerformanceMonitorViewModel _viewModel;

        public PerformanceMonitorView()
        {
            InitializeComponent();
            _viewModel = new PerformanceMonitorViewModel(Dispatcher);
            DataContext = _viewModel;

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Start();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _viewModel.Stop();
        }

        private async void OnTestCpuAlertClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.TriggerTestCpuAlertAndAnalyzeAsync();
        }
    }
}
