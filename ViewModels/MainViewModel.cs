using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaArchiver.Infrastructure;
using MediaArchiver.Models;
using MediaArchiver.Services;
using MediaArchiver.Services.Locators;

namespace MediaArchiver.ViewModels
{
    public class MainViewModel : ObservableBase, ILogger
    {
        private readonly IExifToolLocator _locator = new WindowsExifToolLocator();

        private string _sourceDir = "";
        public string SourceDir
        {
            get => _sourceDir;
            set
            {
                SetField(ref _sourceDir, value);
                OnPropertyChanged(nameof(SourceDirDisplay));
                RefreshFileCount();
                RelayCommand.Refresh();
            }
        }

        public string SourceDirDisplay =>
            string.IsNullOrEmpty(SourceDir) ? "폴더를 선택하세요..." : SourceDir;

        private string _fileCountText = "";
        public string FileCountText
        {
            get => _fileCountText;
            private set => SetField(ref _fileCountText, value);
        }

        private string _exifToolStatus = "ExifTool 확인 중...";
        public string ExifToolStatus
        {
            get => _exifToolStatus;
            private set => SetField(ref _exifToolStatus, value);
        }

        private Brush _exifToolDotColor = Brushes.Gray;
        public Brush ExifToolDotColor
        {
            get => _exifToolDotColor;
            private set => SetField(ref _exifToolDotColor, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            private set => SetField(ref _progressValue, value);
        }

        private string _progressMessage = "";
        public string ProgressMessage
        {
            get => _progressMessage;
            private set => SetField(ref _progressMessage, value);
        }

        private string _progressPercent = "";
        public string ProgressPercent
        {
            get => _progressPercent;
            private set => SetField(ref _progressPercent, value);
        }

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                SetField(ref _isRunning, value);
                OnPropertyChanged(nameof(CanRun));
                RelayCommand.Refresh();
            }
        }

        public bool CanRun => !IsRunning && !string.IsNullOrEmpty(SourceDir);

        private bool _resultVisible;
        public bool ResultVisible
        {
            get => _resultVisible;
            private set => SetField(ref _resultVisible, value);
        }

        private string _resultSuccess = "0";
        private string _resultManual = "0";
        private string _resultFailure = "0";
        private string _resultElapsed = "0초";

        public string ResultSuccess { get => _resultSuccess; private set => SetField(ref _resultSuccess, value); }
        public string ResultManual { get => _resultManual; private set => SetField(ref _resultManual, value); }
        public string ResultFailure { get => _resultFailure; private set => SetField(ref _resultFailure, value); }
        public string ResultElapsed { get => _resultElapsed; private set => SetField(ref _resultElapsed, value); }

        public ObservableCollection<LogEntryViewModel> Logs { get; } = new();
        public event Action? LogAdded;

        public ICommand RunCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ClearLogCommand { get; }

        private CancellationTokenSource? _cts;

        private static readonly System.Collections.Generic.HashSet<string> SupportedExts =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",".jpeg",".png",".heic",".heif",
                ".tiff",".tif",".bmp",".gif",".webp",
                ".cr2",".cr3",".nef",".arw",".dng",".raf",".orf",".rw2",
                ".mp4",".mov",".avi",".mkv",".m4v",".3gp",".wmv", ".mts"
            };

        public MainViewModel()
        {
            RunCommand = new RelayCommand(_ => _ = RunAsync(), _ => CanRun);
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsRunning);
            ClearLogCommand = new RelayCommand(_ => Logs.Clear());

            _ = CheckExifToolAsync();
            Log(LogLevel.Info, "시스템 준비 완료. 대상 폴더를 선택하세요.");
        }

        public void SetSourceDir(string path)
        {
            SourceDir = path;
            Log(LogLevel.Info, $"대상 폴더: {path}");
        }

        private async Task RunAsync()
        {
            var sourceDir = SourceDir;

            IsRunning = true;
            ResultVisible = false;
            ProgressValue = 0;
            ProgressMessage = "";
            ProgressPercent = "";

            _cts = new CancellationTokenSource();

            try
            {
                var result = await Task.Run(() =>
                {
                    var locator = new WindowsExifToolLocator();
                    var metadata = new MetadataService(locator, this);
                    var naming = new NamingService();
                    var engine = new ArchiveEngine(sourceDir, metadata, naming, this);

                    engine.OnProgress = progress =>
                        Application.Current.Dispatcher.InvokeAsync(() => ApplyProgress(progress));

                    return engine.Run(_cts.Token);
                }, _cts.Token);

                ShowResult(result);
            }
            catch (OperationCanceledException)
            {
                Log(LogLevel.Warn, "사용자에 의해 중단되었습니다.");
                ProgressValue = 0;
                ProgressMessage = "";
                ProgressPercent = "";
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"[오류] {ex.Message}");
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                IsRunning = false;
            }
        }

        public void Log(LogLevel level, string message)
        {
            void Append()
            {
                Logs.Add(LogEntryViewModel.Create(level, message));
                LogAdded?.Invoke();
            }

            if (Application.Current.Dispatcher.CheckAccess())
                Append();
            else
                Application.Current.Dispatcher.InvokeAsync(Append);
        }

        private void ApplyProgress(ArchiveProgress p)
        {
            ProgressValue = p.Percentage;
            ProgressMessage = p.Message;
            ProgressPercent = $"{(int)p.Percentage}%";
        }

        private void ShowResult(ArchiveResult r)
        {
            ResultSuccess = r.SuccessCount.ToString();
            ResultManual = r.ManualCheckCount.ToString();
            ResultFailure = r.FailureCount.ToString();
            ResultElapsed = $"{r.Elapsed.TotalSeconds:F1}초";
            ResultVisible = true;
            ProgressValue = 100;
            ProgressMessage = "완료";
            ProgressPercent = "100%";
        }

        private void RefreshFileCount()
        {
            if (string.IsNullOrEmpty(SourceDir)) { FileCountText = ""; return; }
            try
            {
                // 하위 폴더(AllDirectories)까지 전부 스캔하도록 변경
                var count = Directory
                    .EnumerateFiles(SourceDir, "*", SearchOption.AllDirectories)
                    .Count(f => SupportedExts.Contains(Path.GetExtension(f)));
                FileCountText = $"하위 폴더 포함 미디어 파일 {count}개 감지됨";
            }
            catch { FileCountText = ""; }
        }

        private async Task CheckExifToolAsync()
        {
            var available = await Task.Run(() => _locator.IsAvailable());

            ExifToolDotColor = available
                ? Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)))
                : Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));

            ExifToolStatus = available
                ? "ExifTool 연결됨 (폴백 활성)"
                : "ExifTool 없음 (폴백 비활성)";

            if (!available)
                Log(LogLevel.Warn,
                    "ExifTool 미감지. HEIC·RAW 파일의 폴백이 비활성 상태입니다. " +
                    "tools\\exiftool.exe 를 배치하거나 PATH 에 등록하세요.");
        }

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}