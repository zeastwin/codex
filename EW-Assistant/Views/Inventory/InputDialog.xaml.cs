using System.Windows;
using System.Windows.Input;

namespace EW_Assistant.Views.Inventory
{
    /// <summary>
    /// 简洁的输入弹窗，用于库存管理交互。
    /// </summary>
    public partial class InputDialog : Window
    {
        public string InputText { get; private set; }

        public InputDialog(string title, string prompt, string defaultValue)
        {
            InitializeComponent();

            Title = string.IsNullOrWhiteSpace(title) ? "输入" : title;
            PromptTextBlock.Text = prompt ?? string.Empty;
            InputTextBox.Text = defaultValue ?? string.Empty;
            InputTextBox.SelectAll();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            try
            {
                DragMove();
            }
            catch
            {
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            InputText = InputTextBox.Text;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>
        /// 显示输入框并返回文本，取消返回 null。
        /// </summary>
        public static string Show(string title, string prompt, string defaultValue)
        {
            var win = new InputDialog(title, prompt, defaultValue)
            {
                Owner = Application.Current != null ? Application.Current.MainWindow : null
            };

            var result = win.ShowDialog();
            if (result == true)
            {
                return win.InputText;
            }

            return null;
        }
    }
}
