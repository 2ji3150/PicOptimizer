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
        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount);

        TimeSpan ts;
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            if (!vm.Idle.Value) return;
            vm.Idle.Value = false;
            Stopwatch sw = new Stopwatch();
            sw.Start();
            vm.DeltaText.Value = "処理中...";
            Directory.CreateDirectory("GTEMP");
            var dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            IEnumerable<Task> tasks;
            string GetTempFilePath() => Path.Combine("GTEMP", Guid.NewGuid().ToString());

            Action act(string file, string tempfile, string newfile) => () => {
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
                } finally {
                    vm.IncrementCounter();
                }
            };




            switch (vm.Index.Value) {
                default://MozJpeg
                    tasks = GetFiles(new string[] { ".jpg", ".jpeg" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".jpg");
                        return TaskAsync($"{mozjpeg} {f.WQ()} > {tempf.WQ()}", act(f, tempf, newf));
                    }).ToArray();
                    vm.total = tasks.Count();
                    if (vm.total <= 0) return;
                    await Task.WhenAll(tasks);
                    break;
                case 1:// Webp Lossless
                    tasks = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".webp");
                        return TaskAsync($"{enwebp} {f.WQ()} -o {tempf.WQ()}", act(f, tempf, newf));
                    }).ToArray();
                    vm.total = tasks.Count();
                    if (vm.total <= 0) return;
                    await Task.WhenAll(tasks);
                    break;
                case 2:// Decode Webp
                    tasks = GetFiles(new string[] { ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".png");
                        return TaskAsync($"{unwebp} {f.WQ()} -o {tempf.WQ()}", act(f, tempf, newf));
                    }).ToArray();
                    vm.total = tasks.Count();
                    if (vm.total <= 0) return;
                    await Task.WhenAll(tasks);
                    break;
                case 3:// manga
                    if (Directory.Exists("ATEMP")) Directory.Delete("ATEMP", true);
                    Directory.CreateDirectory("ATEMP");
                    #region フェーズ１：展開
                    var compresstasks = new List<Task>();
                    foreach (var (f, index) in GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata).Select((archive, index) => (archive, index + 1))) {
                        var tempdir = Path.Combine("ATEMP", index.ToString());
                        Directory.CreateDirectory(tempdir);
                        await RunProcessAsync($@"/c tools\7z x {f.WQ()} -o{tempdir.WQ()}");

                  //      vm.Current.Value = index;

                    }
                    tasks = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata).Select(async (f, index) => {

                        return Task.Run(async () => {
                            var tempf = $"{tempdir}.rar";
                            var newf = Path.ChangeExtension(f, ".rar");
                            await RunProcessAsync($@"/c ""C:\Program Files\WinRAR\Rar.exe"" a -m5 -md1024m -ep1 -r {tempf} {tempdir}\").ContinueWith(_ => act(f, tempf, newf));
                        });
                    }).ToArray();

                    vm.total = tasks.Count();
                    if (vm.total <= 0) return;


                    vm.DeltaText.Value = "フェーズ１：展開";
                    Directory.CreateDirectory("ATEMP");
                    //   vm.Reset();

                    #endregion
                    /*
                    #region フェーズ２：画像圧縮

                    vm.DeltaText.Value = "フェーズ２：画像圧縮";
                 
                    var compresstask = new List<Task>();
                    foreach (var f in Directory.EnumerateFiles("ATEMP", "*.*", SearchOption.AllDirectories)) {
                        var tempf = GetTempFilePath();
                        var ext = Path.GetExtension(f).ToLower();
                        if (new string[] { ".jpg", ".jpeg" }.Contains(ext)) {
                            var newf = Path.ChangeExtension(f, ".jpg");
                            compresstask.Add(TaskAsync($"{mozjpeg} {f.WQ()} > {tempf.WQ()}", act(f, tempf, newf)));
                        } else if (new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }.Contains(ext)) {
                            var newf = Path.ChangeExtension(f, ".webp");
                            compresstask.Add(TaskAsync($"{enwebp} {f.WQ()} -o {tempf.WQ()}", act(f, tempf, newf)));
                        }
                    }

                    vm.total = compresstask.Count();
                    if (vm.total <= 0) {
                        MessageBox.Show("画像がありません");
                        return;
                    }
                    await Task.WhenAll(compresstask);

                    vm.Reset();
                    #endregion

                    #region フェーズ３：アーカイブ圧縮
                    vm.DeltaText.Value = "フェーズ３：アーカイブ圧縮";
                    vm.total = tasks.Count();
                    await Task.WhenAll(tasks);

                    Directory.Delete("ATEMP", true);
                    #endregion
    */

                    break;
            }

            sw.Stop();
            SystemSounds.Asterisk.Play();
            ts = sw.Elapsed;
            if (vm.total != 0) MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
            vm.Reset();
            vm.Idle.Value = true;
            Debug.WriteLine("sine");
        }

        IEnumerable<string> GetFiles(string[] exts, string[] data) {
            foreach (var d in data) {
                if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                    var files = Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).AsParallel().Where(f => exts.Contains(Path.GetExtension(f).ToLower()));
                    foreach (var f in files) {
                        yield return f;
                    }
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
