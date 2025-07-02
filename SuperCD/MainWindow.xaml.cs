using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using FuzzySharp;

using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Diagnostics;
using System.Text.Json;

using WinForms = System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Windows.Interop;


namespace SuperCD
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private List<string> EntryPointCandidates = new();
        private bool isFirstPathEntered = false;
        private const string INDEX_FILE = "entry_index.txt";
        private const string FAVORITE_PATHS_FILE = "favorite_paths.txt";
        private Dictionary<string, string> IconMap = new();
        private Dictionary<string, string> ProgramMap = new();
        private const string CONFIG_FILE = "supercd_config.json";

        private string UserInput = "";
        private string? CurrentPath = null;



        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Modifier key codes
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const int HOTKEY_ID = 9000;


        public MainWindow()
        {
            InitializeComponent();
            LoadConfig();
            SetupTray();
            this.Hide();
            var helper = new WindowInteropHelper(this);
            RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT, (uint)KeyInterop.VirtualKeyFromKey(Key.S));
            ComponentDispatcher.ThreadPreprocessMessage += ComponentDispatcher_ThreadPreprocessMessage;


            this.PreviewKeyDown += new System.Windows.Input.KeyEventHandler(MainWindow_PreviewKeyDown);
            UnifiedInputBox.TextChanged += UnifiedInputBox_TextChanged;
            this.Loaded += (s, e) => UnifiedInputBox.Focus();

            string currentDir = System.IO.Directory.GetCurrentDirectory();
            SetCurrentPath(null);
            RunFileIndexing();

        }

        private NotifyIcon trayIcon;
        private void SetupTray()
        {
            trayIcon = new NotifyIcon();
            try
            {
                trayIcon.Icon = new System.Drawing.Icon("icon.ico");
            }
            catch
            {

            }
            trayIcon.Visible = true;
            trayIcon.Text = "SuperCD";

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
            contextMenu.Items.Add("Exit", null, (s, e) => {
                trayIcon.Visible = false;
                System.Windows.Application.Current.Shutdown();
            });

            trayIcon.ContextMenuStrip = contextMenu;

            trayIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private void ShowWindow()
        {
            var screen = System.Windows.SystemParameters.WorkArea;
            this.UpdateLayout();
            this.Left = (screen.Width - this.Width) / 2 + screen.Left;
            this.Top = (screen.Height - this.Height) / 2 + screen.Top - 30;
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }
        private void HideAndReset()
        {
            ClearCommand();
            this.Hide();
        }
        private void ClearCommand()
        {
            SetCurrentPath(null);
            FileListBox.ItemsSource = null;
            FileListBox.Items.Clear();
            isFirstPathEntered = false;
        }   


        private void ComponentDispatcher_ThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (msg.message == 0x0312) // WM_HOTKEY
            {
                ShowWindow();
                handled = true;
            }
        }


        private class ConfigRoot
        {
            public Dictionary<string, string> icon_map { get; set; } = new();
            public Dictionary<string, string> program_map { get; set; } = new();
        }

        private void LoadConfig()
        {
            if (File.Exists(CONFIG_FILE))
            {
                try
                {
                    string json = File.ReadAllText(CONFIG_FILE);
                    var root = JsonSerializer.Deserialize<ConfigRoot>(json);
                    IconMap = root.icon_map;
                    ProgramMap = root.program_map;
                }
                catch
                {
                    // pass
                }
            }

            // default icons
            if (!IconMap.ContainsKey("_dir")) IconMap["_dir"] = "📁";
            if (!IconMap.ContainsKey("_file")) IconMap["_file"] = "📄";
        }
        private string GetFileIcon(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLower();
            if (Directory.Exists(path))
                return IconMap.ContainsKey("_dir") ? IconMap["_dir"] : "📁";

            return IconMap.TryGetValue(ext, out var val) ? val :
                   (IconMap.ContainsKey("_file") ? IconMap["_file"] : "📄");
        }


        private async void RunFileIndexing(bool forceRebuild = false)
        {
            LogWindow log = null;


            Dispatcher.Invoke(() =>
            {
                log = new LogWindow();
                log.Show();
            });

            try
            {
                //Debug.WriteLine(">>> STARTING BACKGROUND INDEXING TASK <<<");

                List<string> index = await Task.Run(() =>
                {
                    if (forceRebuild || !File.Exists(INDEX_FILE))
                    {

                        if (!File.Exists(FAVORITE_PATHS_FILE))
                        {
                            string defaultPath = Directory.GetCurrentDirectory();
                            File.WriteAllText(FAVORITE_PATHS_FILE, defaultPath + Environment.NewLine);
                        }
                        string[] favPaths = File.ReadAllLines(FAVORITE_PATHS_FILE);
                        //var built = BuildFileIndex(new[] { "C:\\", "D:\\", "E:\\", "F:\\", "G:\\" }, log);
                        var built = BuildFileIndexFromList(favPaths, log);
                        File.WriteAllLines(INDEX_FILE, built);
                        return built;
                    }
                    else
                    {
                        return File.ReadAllLines(INDEX_FILE).ToList();
                    }
                });

                Dispatcher.Invoke(() =>
                {
                    EntryPointCandidates = index;
                    log?.AppendLog($"Index loaded with {EntryPointCandidates.Count} entries.");
                    //Debug.WriteLine($"Index count: {EntryPointCandidates.Count}");
                });

                await Task.Delay(1000);

                Dispatcher.Invoke(() =>
                {
                    log?.AppendLog("Indexing task completed successfully.");
                    //Debug.WriteLine(">>> BACKGROUND INDEXING TASK COMPLETED <<<");
                });
                await Task.Delay(3000);
                Dispatcher.Invoke(() =>
                {
                    log?.Close();
                });
            }
            catch (Exception ex)
            {
                //Debug.WriteLine("🔥 INDEXING TASK FAILED: " + ex.Message);
                //Debug.WriteLine(ex.StackTrace);
            }
        }


        private void SafeScanDirectory(string path, List<string> index, LogWindow log)
        {
            try
            {
                index.Add(path);
                log?.Dispatcher.Invoke(() => log.AppendLog(path));

                foreach (var sub in Directory.EnumerateDirectories(path))
                {
                    SafeScanDirectory(sub, index, log); // 再帰呼び出し
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"⚠ Skipped: {path} → {ex.Message}");
                log?.Dispatcher.Invoke(() => log.AppendLog($"⚠ Skipped: {path} → {ex.Message}"));
            }
        }

        private List<string> BuildFileIndexFromList(IEnumerable<string> baseDirs, LogWindow log = null)
        {
            List<string> index = new();

            foreach (var basePath in baseDirs)
            {
                if (!Directory.Exists(basePath))
                {
                    log?.AppendLog($"❌ Skipped missing path: {basePath}");
                    continue;
                }

                log?.AppendLog($"🔍 Scanning: {basePath}");

                try
                {
                    index.Add(basePath);
                    foreach (var dir in Directory.EnumerateDirectories(basePath, "*", SearchOption.AllDirectories))
                    {
                        index.Add(dir);
                        log?.AppendLog(dir);
                    }
                    foreach (var file in Directory.EnumerateFiles(basePath, "*", SearchOption.AllDirectories))
                    {
                        index.Add(file);
                        log?.AppendLog(file);
                    }
                }
                catch (Exception ex)
                {
                    log?.AppendLog($"⚠ Skipped: {basePath} → {ex.Message}");
                }
            }

            log?.AppendLog($"✅ Build complete: {index.Count} entries");
            return index;
        }

        private List<string> BuildFileIndex(string[] roots, LogWindow log = null)
        {
            List<string> index = new();
            //Debug.WriteLine("Starting file index build...");

            foreach (var root in roots)
            {
                Debug.WriteLine($"Checking root: {root} Exists? => {Directory.Exists(root)}");
                SafeScanDirectory(root, index, log);
            }

            // WSL root scanning
            try
            {
                string wslRoot = @"\\wsl$";
                if (Directory.Exists(wslRoot))
                {
                    foreach (var distro in Directory.GetDirectories(wslRoot))
                    {
                        string distroName = System.IO.Path.GetFileName(distro);
                        string[] commonRoots = { "home", "mnt\\c", "mnt\\d", "mnt\\e" };

                        foreach (var sub in commonRoots)
                        {
                            string basePath = System.IO.Path.Combine(distro, sub);
                            if (!Directory.Exists(basePath)) continue;

                            SafeScanDirectory(basePath, index, log);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //Debug.WriteLine($"WSL root error: {ex.Message}");
            }

            return index;
        }


        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                HideAndReset();
                e.Handled = true;
            }
            else if ((e.Key == Key.Up && Keyboard.Modifiers == ModifierKeys.None) ||
    (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (FileListBox.SelectedIndex > 0)
                    FileListBox.SelectedIndex--;
                FileListBox.ScrollIntoView(FileListBox.SelectedItem);
                e.Handled = true;
            }
            else if ((e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.None) ||
         (e.Key == Key.J && Keyboard.Modifiers == ModifierKeys.Control))
            {
                if (FileListBox.SelectedIndex < FileListBox.Items.Count - 1)
                    FileListBox.SelectedIndex++;
                FileListBox.ScrollIntoView(FileListBox.SelectedItem);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {

                //Debug.WriteLine("=== ENTER KEY PRESSED ===");

                if (FileListBox.SelectedItem is ListBoxItem selectedItem)
                {
                    //Debug.WriteLine($"[ListBoxItem] Tag: {selectedItem.Tag}");
                    //Debug.WriteLine($"[ListBoxItem] Content: {selectedItem.Content}");
                }
                else if (FileListBox.SelectedItem != null)
                {
                    //Debug.WriteLine($"[Other] SelectedItem Type: {FileListBox.SelectedItem.GetType().Name}");
                    //Debug.WriteLine($"[Other] SelectedItem Value: {FileListBox.SelectedItem}");
                }
                else
                {
                    //Debug.WriteLine("[ENTER] No item selected.");
                }



                string basePath = CurrentPath ?? "";
                string input = UserInput.Trim();
                string combinedPath = System.IO.Path.Combine(basePath, input);

                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    if (FileListBox.SelectedItem is ListBoxItem item && item.Tag is string tagPath)
                    {
                        OpenInExplorer(tagPath);
                        e.Handled = true;
                    }
                    else if (FileListBox.SelectedItem is string selectedText)
                    {
                        OpenInExplorer(combinedPath);
                        e.Handled = true;
                    }
                }
                else
                {
                    if (FileListBox.SelectedItem is ListBoxItem item && item.Tag is string tagPath)
                    {
                        HandlePathSelection(tagPath);
                        e.Handled = true;
                    }
                    else if (FileListBox.SelectedItem is string selectedText)
                    {
                        HandlePathSelection(combinedPath);
                        e.Handled = true;
                    }
                }
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (!string.IsNullOrEmpty(CurrentPath))
                {
                    string parent = Directory.GetParent(CurrentPath)?.FullName;

                    if (!string.IsNullOrEmpty(parent))
                    {
                        SetCurrentPath(parent);
                        UpdateFileList(parent);
                    }
                }
                e.Handled = true;
            }
            else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
            {
                ClearCommand();
                e.Handled = true;
            }
            else if (e.Key == Key.R && Keyboard.Modifiers == ModifierKeys.Control)
            {
                RunFileIndexing(forceRebuild: true);
                e.Handled = true;
            }

        }
        private void OpenInExplorer(string path)
        {
            string explorerTarget = "";

            if (File.Exists(path))
            {
                explorerTarget = System.IO.Path.GetDirectoryName(path);
            }
            else if (Directory.Exists(path))
            {
                explorerTarget = path;
            }

            if (!string.IsNullOrEmpty(explorerTarget))
            {
                System.Diagnostics.Process.Start("explorer.exe", $"\"{explorerTarget}\"");
            }
            HideAndReset();
        }

        private void HandlePathSelection(string path)
        {
            if (Directory.Exists(path))
            {
                SetCurrentPath(path + "\\");
                UpdateFileList(path);
                isFirstPathEntered = true;
            }
            else if (File.Exists(path))
            {
                OpenFileWithAssignedSoftware(path);
            }
            else
            {
                System.Windows.MessageBox.Show("err", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string ResolveInitialPath(string input)
        {
            string trimmed = input.Trim();

            if (System.IO.Path.IsPathRooted(trimmed) && Directory.Exists(trimmed))
            {
                return trimmed;
            }

            var bestMatch = EntryPointCandidates
                .Select(path => new {
                    Path = path,
                    Score = path
                        .Split(System.IO.Path.DirectorySeparatorChar)
                        .Select(segment => Fuzz.Ratio(segment.ToLower(), trimmed))
                        .Max()
                })
                .Where(x => x.Score > 60)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();
            if (bestMatch != null)
            {
                string resolved = bestMatch.Path;

                if (Directory.Exists(resolved))
                {
                    UpdateFileList(resolved);
                }
                else if (File.Exists(resolved))
                {
                    FileListBox.Items.Clear();

                    string ext = System.IO.Path.GetExtension(resolved).ToLower();
                    string icon = IconMap.TryGetValue(ext, out var val) ? val : "";
                    string name = $"{icon} {System.IO.Path.GetFileName(resolved)}";

                    var item = new ListBoxItem
                    {
                        Content = name,
                        Tag = resolved
                    };

                    FileListBox.Items.Add(item);
                    FileListBox.SelectedIndex = 0;
                }

                return resolved;
            }

            return trimmed;
        }

        private void UpdateUnifiedInput(string input)
        {
            UserInput = input;
            string pathDisplay=string.IsNullOrEmpty(CurrentPath) ? "" : CurrentPath + "\\";
            UnifiedInputBox.Text = "> " + pathDisplay + UserInput;
            UnifiedInputBox.CaretIndex = UnifiedInputBox.Text.Length;
        }

        private void SetCurrentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                CurrentPath = null;
            else
                CurrentPath = path.TrimEnd('\\');

            UpdateUnifiedInput("");
        }

        private void UnifiedInputBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string fullText = UnifiedInputBox.Text;
            string prefix = "> " + (string.IsNullOrEmpty(CurrentPath)? "":CurrentPath+"\\");

            if (fullText.StartsWith(prefix))
            {
                UserInput = fullText.Substring(prefix.Length);
            }
            else
            {
                UnifiedInputBox.Text = prefix + UserInput;
                UnifiedInputBox.CaretIndex = UnifiedInputBox.Text.Length;
                return;
            }
            string keyword = UserInput.ToLower();

            if (string.IsNullOrWhiteSpace(UserInput))
            {
                if (CurrentPath == null)
                {
                    FileListBox.Items.Clear();
                }
                else
                {
                    UpdateFileList(CurrentPath);
                }
                return;
            }
            if (!isFirstPathEntered)
            {
                FileListBox.Items.Clear();

                var results = EntryPointCandidates
                    .Select(path =>
                    {
                        string lowerPath = path.ToLower();
                        int fullScore = Fuzz.Ratio(lowerPath, keyword);
                        int partialScore = Fuzz.PartialRatio(lowerPath, keyword);
                        int segmentScore = path
                            .Split(System.IO.Path.DirectorySeparatorChar)
                            .Select(segment => Fuzz.Ratio(segment.ToLower(), keyword))
                            .Max();

                        int finalScore = Math.Max(Math.Max(fullScore, partialScore), segmentScore);
                        return new { Path = path, Score = finalScore };
                    })
                    .Where(x => x.Score > 70)
                    .OrderByDescending(x => x.Score);

                foreach (var x in results)
                {
                    string icon = Directory.Exists(x.Path) ? "" : GetFileIcon(x.Path);
                    string path = x.Path;

                    TextBlock tb = new TextBlock
                    {
                        FontFamily = new System.Windows.Media.FontFamily("Agave Nerd Font"),
                        FontSize = 16,
                        Foreground = System.Windows.Media.Brushes.Transparent,
                        Tag = path,
                        TextWrapping = TextWrapping.Wrap
                    };

                    tb.Inlines.Add(new Run(icon + " ")
                    {
                        Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#66FF66"))
                    });

                    int matchIndex = path.ToLower().IndexOf(keyword.ToLower());
                    if (matchIndex >= 0)
                    {
                        tb.Inlines.Add(new Run(path.Substring(0, matchIndex))
                        {
                            Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#66FF66"))
                        });

                        var highlighted = new TextBlock(new Run(path.Substring(matchIndex, keyword.Length)))
                        {
                            Background = System.Windows.Media.Brushes.Gray,
                            Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#66FF66")),
                            FontWeight = FontWeights.Bold,
                            Padding = new Thickness(1, 0, 1, 0),
                        };
                        tb.Inlines.Add(new InlineUIContainer(highlighted));

                        tb.Inlines.Add(new Run(path.Substring(matchIndex + keyword.Length))
                        {
                            Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#66FF66"))
                        });
                    }
                    else
                    {
                        tb.Inlines.Add(new Run(path)
                        {
                            Foreground = (SolidColorBrush)(new BrushConverter().ConvertFrom("#66FF66"))
                        });
                    }

                    var item = new ListBoxItem
                    {
                        Content = tb,
                        Tag = path
                    };

                    FileListBox.Items.Add(item);
                }

                if (FileListBox.Items.Count > 0)
                    FileListBox.SelectedIndex = 0;
            }
            else
            {
                var allItems = FileListBox.Items.Cast<ListBoxItem>().ToList();
                FileListBox.Items.Clear();

                var filtered = allItems
                    .Select(item => new
                    {
                        Item = item,
                        Text = item.Content.ToString(),
                        Score = item.Content.ToString().ToLower().Contains(keyword)
                                ? 100
                                : Fuzz.Ratio(item.Content.ToString().ToLower(), keyword)
                    })
                    .Where(x => x.Score > 70)
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Item);

                foreach (var item in filtered)
                {
                    FileListBox.Items.Add(item);
                }

                if (FileListBox.Items.Count > 0)
                    FileListBox.SelectedIndex = 0;

            }

        }

        private void UpdateFileList(string path)
        {
            FileListBox.ItemsSource = null;  // Disable data binding
            FileListBox.Items.Clear();       // Clear manually-added items

            foreach (var dir in Directory.GetDirectories(path))
            {
                string name = " " + System.IO.Path.GetFileName(dir);
                var item = new ListBoxItem
                {
                    Content = name,
                    Tag = dir
                };
                FileListBox.Items.Add(item);
            }

            foreach (var file in Directory.GetFiles(path))
            {
                string ext = System.IO.Path.GetExtension(file).ToLower();
                string icon = IconMap.TryGetValue(ext, out var val) ? val : "";
                string name = $"{icon} {System.IO.Path.GetFileName(file)}";
                var item = new ListBoxItem
                {
                    Content = name,
                    Tag = file
                };
                FileListBox.Items.Add(item);
            }

            if (FileListBox.Items.Count > 0)
                FileListBox.SelectedIndex = 0;
        }



        private void OpenFileWithAssignedSoftware(string filePath)
        {
            string ext = System.IO.Path.GetExtension(filePath).ToLower();

            if (ProgramMap.TryGetValue(ext, out string programPath))
            {
                System.Diagnostics.Process.Start(programPath, $"\"{filePath}\"");
            }
            else
            {
                System.Diagnostics.Process.Start(new ProcessStartInfo(filePath)
                {
                    UseShellExecute = true
                });
            }
            HideAndReset();
        }


    }
}
