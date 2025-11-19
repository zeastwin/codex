using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace EW_Assistant
{
    public partial class App : Application
    {
        public App()
        {
            // 非 UI 线程的兜底
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void Application_DispatcherUnhandledException(
     object sender,
     DispatcherUnhandledExceptionEventArgs e)
        {
            var ex = e.Exception;
            var msg = ex != null ? ex.Message : "发生了未知错误。";

            try
            {
                global::EW_Assistant.MainWindow.PostProgramInfo("UI 线程异常：" + msg, "error");
            }
            catch
            {
                // 确保这里绝对不再抛异常
            }

            MessageBox.Show(
                msg,
                "程序运行出错",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            var msg = ex != null ? ex.ToString() : "非 UI 线程发生未知错误。";

            try
            {
                global::EW_Assistant.MainWindow.PostProgramInfo("非 UI 线程异常：" + msg, "error");
            }
            catch
            {
            }
        }


        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                global::EW_Assistant.MainWindow.PostProgramInfo("任务调度异常：" + e.Exception, "error");
            }
            catch
            {
            }

            // 如果不希望因为这个导致进程终止：
            e.SetObserved();
        }
    }
}
