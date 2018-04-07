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
        const string cwebp = @"tools\cwebp", cwebp_sw = "-lossless -m 6 -q 100 -mt";
        const string dwebp = @"tools\dwebp", dwebp_sw = "-mt";
        const string mozjpeg = @"tools\jpegtran-static", mozjpeg_sw = "-copy all";
        const string winrar = @"C:\Program Files\WinRAR\Rar", winrar_sw = "a -m5 -md1024m -ep1 -r";
        const string senvenzip = @"tools\7z", senvenzip_sw = "x";

        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount - 2);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            vm.Idle.Value = false;
            sw.Restart();
            string[] dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            Task[] tasks;
            long totaldelta = 0;
            int counter = 0, index = 0;

            string time = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string tmp_g = Path.Combine("TEMP", time, "G"), tmp_a = Path.Combine("TEMP", time, "A");
            Directory.CreateDirectory(tmp_g);


            string GetTempFilePath(ref int idnum) => Path.Combine(tmp_g, (++idnum).ToString());

            long Replace(ref long td, string file, string tempfile, string ext) {
                try {
                    FileInfo fiI = new FileInfo(file) { IsReadOnly = false }, fiT = new FileInfo(tempfile);
                    if (fiT.Length <= 0) throw new Exception("生成したファイルが破損");
                    long delta = fiI.Length - fiT.Length;
                    fiI.Delete();
                    fiT.MoveTo(Path.ChangeExtension(file, ext));
                    if (delta == 0) return td;
                    else return Interlocked.Add(ref td, delta);
                } catch (Exception ex) {
                    MessageBox.Show($"エラー! {file} ： {ex.Message}");
                    return td;
                }
            }


            switch (vm.Index.Value) {
                default://MozJpeg
                    tasks = GetFiles(new string[] { ".jpg", ".jpeg" }, dropdata).Select(inf => {
                        string outf = GetTempFilePath(ref index);
                        return TaskAsync(mozjpeg, new string[] { mozjpeg_sw, "-outfile", outf.WQ(), inf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".jpg"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 1:// cwebp
                    tasks = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, dropdata).Select(inf => {
                        string outf = GetTempFilePath(ref index);
                        return TaskAsync(cwebp, new string[] { cwebp_sw, inf.WQ(), "-o", outf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".webp"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 2:// dwebp
                    tasks = GetFiles(new string[] { ".webp" }, dropdata).Select(inf => {
                        string outf = GetTempFilePath(ref index);
                        return TaskAsync(dwebp, new string[] { dwebp_sw, inf.WQ(), "-o", outf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".png"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 3:// manga
                    string[] dropfiles = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata).ToArray();
                    if (!vm.Start(dropfiles.Length)) return;
                    DirectoryInfo di = new DirectoryInfo(tmp_a);

                    foreach (string a in dropfiles) {
                        di.Create();
                        await RunProcessAsync(senvenzip, new string[] { senvenzip_sw, a.WQ(), "-o" + tmp_a.WQ() });
                        #region Ruduce Top Level
                        string topdir = tmp_a;
                        DirectoryInfo t_di = di;
                        while (t_di.EnumerateDirectories().Take(2).Count() == 1 && !t_di.EnumerateFiles().Any()) {
                            topdir += @"\" + t_di.EnumerateDirectories().First().Name;
                            t_di = new DirectoryInfo(topdir);
                        }
                        #endregion

                        List<Task> optimizetasklist = new List<Task>();
                        long tempdelta = totaldelta;

                        foreach (string inf in Directory.EnumerateFiles(topdir, "*.*", SearchOption.AllDirectories)) {
                            string outf;
                            string ext = Path.GetExtension(inf).ToLower();
                            if (new string[] { ".jpg", ".jpeg" }.Contains(ext)) {
                                outf = GetTempFilePath(ref index);
                                optimizetasklist.Add(TaskAsync(mozjpeg, new string[] { mozjpeg_sw, "-outfile", outf.WQ(), inf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref tempdelta, inf, outf, ".jpg"), counter)));
                            } else if (new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }.Contains(ext)) {
                                outf = GetTempFilePath(ref index);
                                optimizetasklist.Add(TaskAsync(cwebp, new string[] { cwebp_sw, inf.WQ(), "-o", outf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref tempdelta, inf, outf, ".webp"), counter)));
                            }
                        }
                        await Task.WhenAll(optimizetasklist);
                        string outa = tmp_a + ".rar";
                        await RunProcessAsync(winrar.WQ(), new string[] { winrar_sw, outa.WQ(), $@"{topdir}\".WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, a, outa, ".rar"), ++counter));
                        di.Delete(true);
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
            foreach (string d in data) {
                if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                    IEnumerable<string> files = Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).Where(f => exts.Contains(Path.GetExtension(f).ToLower()));
                    foreach (string f in files) yield return f;
                } else if (exts.Contains(Path.GetExtension(d).ToLower())) yield return d;
            }
        }

        async Task TaskAsync(string exe, string[] arg) {
            await sem.WaitAsync();
            try {
                await RunProcessAsync(exe, arg);
            } finally {
                sem.Release();
            }
        }

        Task RunProcessAsync(string filename, string[] arguments) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Process p = new Process {
                StartInfo = { FileName = filename, Arguments = string.Join(" ", arguments), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true },
                EnableRaisingEvents = true
            };
            p.Exited += (sender, args) => {
                tcs.SetResult(true);
                p.Dispose();
            };
            p.OutputDataReceived += (sender, args) => Debug.WriteLine(args.Data);
            p.Start();
            p.BeginOutputReadLine();
            return tcs.Task;
        }
    }
    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }
}
