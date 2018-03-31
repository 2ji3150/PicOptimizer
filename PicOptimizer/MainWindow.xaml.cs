using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PicOptimizer {
    public partial class MainWindow : Window {
        public MainWindow() {
            InitializeComponent();
            DataContext = vm;
        }

        ViewModel vm = new ViewModel();

        const string enwebp = @"/c tools\cwebp -quiet -lossless -m 6 -q 100 -mt";
        const string unwebp = @"/c tools\dwebp -mt";
        const string mozjpeg = @"/c tools\jpegtran-static -copy all";
        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount - 2);

        TimeSpan ts;
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            vm.Idle.Value = false;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Directory.CreateDirectory("GTEMP");
            var dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            IEnumerable<Task> tasks;
            string GetTempFilePath() => Path.Combine("GTEMP", Guid.NewGuid().ToString());

            Action ReplaceWithCal(string file, string tempfile, string newfile) => () => {
                try {
                    FileInfo fiI = new FileInfo(file), fiT = new FileInfo(tempfile);
                    if (fiT.Length > 0) {
                        var delta = fiI.Length - fiT.Length;
                        if (delta != 0) vm.AddDelta(delta);
                        fiI.IsReadOnly = false;
                        fiI.Delete();
                        fiT.MoveTo(newfile);
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error! {file} on {ex.Message}");
                }
            };

            Action Replace(string file, string tempfile, string newfile) => () => {
                try {
                    FileInfo fiI = new FileInfo(file), fiT = new FileInfo(tempfile);
                    if (fiT.Length > 0) {
                        fiI.IsReadOnly = false;
                        fiI.Delete();
                        fiT.MoveTo(newfile);
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"Error! {file} on {ex.Message}");
                }
            };

            switch (vm.Index.Value) {
                default://MozJpeg
                    tasks = GetFiles(new string[] { ".jpg", ".jpeg" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".jpg");
                        return TaskAsync($"{mozjpeg} {f.WQ()} > {tempf.WQ()}", ReplaceWithCal(f, tempf, newf)).ContinueWith(_ => vm.IncrementCounter());
                    }).ToArray();
                    vm.total = tasks.Count();
                    if (vm.total <= 0) break;
                    vm.Ptext.Value = $"0 / {vm.total}";
                    await Task.WhenAll(tasks);
                    break;
                case 1:// Webp Lossless
                    tasks = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".webp");
                        return TaskAsync($"{enwebp} {f.WQ()} -o {tempf.WQ()}", ReplaceWithCal(f, tempf, newf)).ContinueWith(_ => vm.IncrementCounter()); ;
                    }).ToArray();
                    vm.total = tasks.Count();
                    if (vm.total == 0) break;
                    await Task.WhenAll(tasks);
                    break;
                case 2:// Decode Webp
                    tasks = GetFiles(new string[] { ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".png");
                        return TaskAsync($"{unwebp} {f.WQ()} -o {tempf.WQ()}", ReplaceWithCal(f, tempf, newf)).ContinueWith(_ => vm.IncrementCounter()); ;
                    }).ToArray();
                    vm.total = tasks.Count();
                    if (vm.total == 0) break;
                    vm.Ptext.Value = $"0 / {vm.total}";
                    await Task.WhenAll(tasks);
                    break;
                case 3:// manga
                    if (Directory.Exists("ATEMP")) Directory.Delete("ATEMP", true);
                    Directory.CreateDirectory("ATEMP");
                    var files = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata).ToArray();
                    vm.total = files.Count();
                    if (vm.total == 0) return;
                    vm.Ptext.Value = $"0 / {vm.total}";
                    for (int i = 0; i < files.Length; i++) {
                        var tempdir = Path.Combine("ATEMP", i.ToString());
                        Directory.CreateDirectory(tempdir);
                        await RunProcessAsync($@"/c tools\7z x {files[i].WQ()} -o{tempdir.WQ()}");
                        var temprar = $"{tempdir}.rar";
                        var newrar = Path.ChangeExtension(files[i], ".rar");
                        var optimizetasklist = new List<Task>();
                        foreach (var f in Directory.EnumerateFiles(tempdir, "*.*", SearchOption.AllDirectories)) {
                            var tempf = GetTempFilePath();
                            var ext = Path.GetExtension(f).ToLower();
                            if (new string[] { ".jpg", ".jpeg" }.Contains(ext)) {
                                var newf = Path.ChangeExtension(f, ".jpg");
                                optimizetasklist.Add(TaskAsync($"{mozjpeg} {f.WQ()} > {tempf.WQ()}", Replace(f, tempf, newf)));
                            } else if (new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }.Contains(ext)) {
                                var newf = Path.ChangeExtension(f, ".webp");
                                optimizetasklist.Add(TaskAsync($"{enwebp} {f.WQ()} -o {tempf.WQ()}", Replace(f, tempf, newf)));
                            }
                        }
                        await Task.WhenAll(optimizetasklist);
                        await RunProcessAsync($@"/c ""C:\Program Files\WinRAR\Rar.exe"" a -m5 -md1024m -ep1 -r {temprar} {tempdir}\").ContinueWith(t => ReplaceWithCal(files[i], temprar, newrar)());
                        vm.Current.Value = i + 1;
                    }
                    break;
            }

            sw.Stop();
            SystemSounds.Asterisk.Play();
            ts = sw.Elapsed;
            if (vm.total != 0) MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
            vm.Reset();
            vm.Idle.Value = true;
        }

        IEnumerable<string> GetFiles(string[] exts, string[] data) {
            foreach (var d in data) {
                if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                    var files = Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).AsParallel().Where(f => exts.Contains(Path.GetExtension(f).ToLower()));
                    foreach (var f in files) yield return f;
                } else if (exts.Contains(Path.GetExtension(d).ToLower())) yield return d;
            }
        }

        async Task TaskAsync(string arg, Action action) {
            await sem.WaitAsync();
            try {
                await RunProcessAsync(arg).ContinueWith(_ => action());
            } finally {
                sem.Release();
            }
        }

        Task RunProcessAsync(string arg) {
            var tcs = new TaskCompletionSource<bool>();
            var process = new Process {
                StartInfo = { FileName = "cmd.exe", Arguments = arg, UseShellExecute = false, CreateNoWindow = true },
                EnableRaisingEvents = true
            };
            process.Exited += (sender, args) => {
                tcs.SetResult(true);
                process.Dispose();
            };
            process.Start();
            return tcs.Task;
        }
    }
    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }
}
