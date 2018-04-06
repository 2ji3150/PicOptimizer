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
        const string cwebp = @"tools\cwebp";
        const string dwebp = @"tools\dwebp";
        const string mozjpeg = @"tools\jpegtran-static";
        const string winrar = @"""%ProgramFiles%\WinRAR\Rar""";
        const string senvenzip = @"tools\7z";
        const string tempdir = "TEMP";


        //const string enwebp = @" / c tools\cwebp -quiet -lossless -m 6 -q 100 -mt";
        //const string unwebp = @"/c tools\dwebp -mt";
        //const string mozjpeg = @"/c tools\jpegtran-static -copy all";
        //const string winrar = @"/c ""%ProgramFiles%\WinRAR\Rar"" a -m5 -md1024m -ep1 -r";
        //const string senvenzip = @"tools\7z";
        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount - 2);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            vm.Idle.Value = false;
            sw.Restart();
            Directory.CreateDirectory(tempdir);
            var dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            Task[] tasks;
            long totaldelta = 0;
            int counter = 0;
            string GetTempFilePath() => Path.Combine(tempdir, Guid.NewGuid().ToString());

            long Replace(string file, string tempfile, string ext) {
                try {
                    FileInfo fiI = new FileInfo(file) { IsReadOnly = false }, fiT = new FileInfo(tempfile);
                    if (fiT.Length <= 0) throw new Exception("生成したファイルが破損");
                    var delta = fiI.Length - fiT.Length;
                    fiI.Delete();
                    fiT.MoveTo(Path.ChangeExtension(file, ext));
                    return delta;
                } catch (Exception ex) {
                    MessageBox.Show($"エラー! {file} ： {ex.Message}");
                    return 0;
                }
            }


            switch (vm.Index.Value) {
                default://MozJpeg
                    tasks = GetFiles(new string[] { ".jpg", ".jpeg" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        return TaskAsync(mozjpeg, new string[] { "-copy all", f.WQ(), ">", tempf.WQ() }, () => vm.Update(Interlocked.Add(ref totaldelta, Replace(f, tempf, ".jpg")), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 1:// Webp Lossless
                    tasks = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        return TaskAsync(cwebp, new string[] { "-lossless -m 6 -q 100 -mt", f.WQ(), "-o", tempf.WQ() }, () => vm.Update(Interlocked.Add(ref totaldelta, Replace(f, tempf, ".webp")), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 2:// Decode Webp
                    tasks = GetFiles(new string[] { ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        return TaskAsync(dwebp, new string[] { "-mt", f.WQ(), "-o", tempf.WQ() }, () => vm.Update(Interlocked.Add(ref totaldelta, Replace(f, tempf, ".png")), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 3:// manga
                    var dropfiles = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata).ToArray();
                    if (!vm.Start(dropfiles.Length)) return;
                    DirectoryInfo di = new DirectoryInfo(@"TEMP\A");
                    if (di.Exists) di.Delete(true);
                    di.Create();
                    foreach (var a in dropfiles) {
                        var tempdir = @"TEMP\A";
                        var temprar = $"{tempdir}.rar";
                        Directory.CreateDirectory(tempdir);
                        await RunProcessAsync(senvenzip, new string[] { "x", a.WQ(), @"-oTEMP\A" });
                        while (!di.EnumerateFiles().Any() && di.EnumerateDirectories().Count() == 1) {
                            tempdir = Path.Combine(tempdir, di.EnumerateDirectories().ToArray()[0].Name);
                            di = new DirectoryInfo(tempdir);
                        }
                        var optimizetasklist = new List<Task>();
                        long tempdelta = totaldelta;
                        foreach (string f in Directory.EnumerateFiles(tempdir, "*.*", SearchOption.AllDirectories)) {
                            string tempf = GetTempFilePath();
                            string ext = Path.GetExtension(f).ToLower();
                            if (new string[] { ".jpg", ".jpeg" }.Contains(ext)) optimizetasklist.Add(TaskAsync(mozjpeg, new string[] { "-copy all", f.WQ(), ">", tempf.WQ() }, () => vm.Update(Interlocked.Add(ref tempdelta, Replace(f, tempf, ".jpg")), counter)));
                            else if (new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }.Contains(ext)) optimizetasklist.Add(TaskAsync(cwebp, new string[] { "-lossless -m 6 -q 100 -mt", f.WQ(), "-o", tempf.WQ() }, () => vm.Update(Interlocked.Add(ref tempdelta, Replace(f, tempf, ".webp")), counter)));
                        }
                        await Task.WhenAll(optimizetasklist);
                        await RunProcessAsync(winrar, new string[] { "a -m5 -md1024m -ep1 -r", temprar.WQ(), $@"{tempdir}\".WQ() }).ContinueWith(_ => vm.Update(totaldelta += Replace(a, temprar, ".rar"), ++counter));
                    }
                    break;
            }
            sw.Stop();
            TimeSpan ts = sw.Elapsed;
            SystemSounds.Asterisk.Play();
            MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
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

        async Task TaskAsync(string exe, string[] arg, Action action) {
            await sem.WaitAsync();
            try {
                await RunProcessAsync(exe, arg).ContinueWith(_ => action());
            } finally {
                sem.Release();
            }
        }

        Task RunProcessAsync(string filename, string[] arguments) {
            var tcs = new TaskCompletionSource<bool>();
            var process = new Process {
                StartInfo = { FileName = filename, Arguments = string.Join(" ", arguments), UseShellExecute = false, CreateNoWindow = true },
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
