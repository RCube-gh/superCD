using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SuperCD
{
    public partial class LogWindow : Window
    {
        private readonly ConcurrentQueue<string> logQueue = new();
        private CancellationTokenSource monitorCts = new();
        private Task monitorTask;

        public LogWindow()
        {
            InitializeComponent();
            StartMonitoring();
        }
        private void ScrollArea_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                this.DragMove();
        }
        private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var main = System.Windows.Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                main?.Dispatcher.Invoke(() =>
                {
                    main.Hide();
                });
                e.Handled = true;
            }
        }


        public void AppendLog(string line)
        {
            logQueue.Enqueue(line);
        }

        private void StartMonitoring()
        {
            var token = monitorCts.Token;

            monitorTask = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    if (logQueue.TryDequeue(out var line))
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var text = new TextBlock
                            {
                                Text = line,
                                Foreground = System.Windows.Media.Brushes.LightGreen,
                                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                                FontSize = 14,
                                Margin = new Thickness(0, 2, 0, 2)
                            };
                            LogPanel.Children.Add(text);
                            ScrollArea.ScrollToEnd();
                        });
                    }
                    else
                    {
                        await Task.Delay(10, token); // 読み込みがないときはちょっと休む
                    }
                }
            }, token);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            monitorCts.Cancel();
            base.OnClosing(e);
        }
    }
}
