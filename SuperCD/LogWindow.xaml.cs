using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;

namespace SuperCD
{
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
        }
        public void AppendLog(string line)
        {
            File.AppendAllText("log_debug.txt", line + Environment.NewLine);

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => AppendLog(line));
                return;
            }

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
        }

    }
}
