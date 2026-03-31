using System.Windows;
using MediaArchiver.ViewModels;

namespace MediaArchiver.Views
{
    public partial class MainWindow : Window
    {
        private MainViewModel Vm => (MainViewModel)DataContext;

        public MainWindow()
        {
            InitializeComponent();

            // ViewModel 의 LogAdded 이벤트를 구독해 자동 스크롤
            Loaded += (_, _) => Vm.LogAdded += ScrollLogToEnd;
            Unloaded += (_, _) => Vm.LogAdded -= ScrollLogToEnd;
        }

        // ── 폴더 탐색 ─────────────────────────────────────────────────

        private void BtnBrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var path = BrowseFolder("대상 폴더 선택");
            if (path != null) Vm.SetSourceDir(path);
        }

        private static string? BrowseFolder(string title)
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = title,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK
                ? dlg.SelectedPath
                : null;
        }

        // ── 자동 스크롤 ───────────────────────────────────────────────

        private void ScrollLogToEnd() => LogScroll.ScrollToEnd();
    }
}