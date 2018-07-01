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
        public MainWindow() => InitializeComponent();
        const string cwebp = @"tools\cwebp", cwebp_sw = "-quiet -mt -lossless -m 6 -q 100";
        const string dwebp = @"tools\dwebp", dwebp_sw = "-quiet -mt";
        const string mozjpeg = @"tools\jpegtran-static", mozjpeg_sw = "-copy all";
        const string winrar = @"tools\Rar", winrar_sw = "a -m5 -md1024m -ep1 -r -idq";
        const string senvenzip = @"tools\7z", senvenzip_sw = "x";
        readonly HashSet<string>[] exts = new HashSet<string>[] {
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bmp", ".png", ".tif", "tiff", ".webp" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".webp" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" }
        };
        SemaphoreSlim sem = new SemaphoreSlim(12);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            ViewModel vm = (ViewModel)DataContext;
            vm.Idle.Value = false;
            sw.Restart();
            string[] dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            Task[] tasks;
            long totaldelta = 0;
            int counter = 0;
            string tmp_now = Path.Combine("TEMP", DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
            Directory.CreateDirectory(tmp_now);

            #region ローカル関数
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

            int i = 0;
            IEnumerable<(string inf, string outf)> GetFiles() {
                bool checkext(string file) => exts[vm.Index.Value].Contains(Path.GetExtension(file));
                string outf() => Path.Combine(tmp_now, (++i).ToString());
                foreach (string d in dropdata) {
                    if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                        foreach (string f in Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).AsParallel().Where(f => checkext(f))) yield return (f, outf());
                    } else if (checkext(d)) yield return (d, outf());
                }
            }

            async Task TaskAsync(string exe, string arg) {
                await sem.WaitAsync();
                try {
                    await new Process { StartInfo = { FileName = exe, Arguments = arg, UseShellExecute = false, CreateNoWindow = true } }.WaitForExitAsync();
                } finally {
                    sem.Release();
                }
            }
            Mutex mut = new Mutex();
            async Task TaskAsyncMut(string exe, string arg) {
                mut.WaitOne();
                try {
                    await new Process { StartInfo = { FileName = exe, Arguments = arg, UseShellExecute = false, CreateNoWindow = true } }.WaitForExitAsync();
                } finally {
                    mut.ReleaseMutex();
                }
            }
            #endregion

            switch (vm.Index.Value) {
                default://MozJpeg
                    tasks = GetFiles().Select(x => TaskAsync(mozjpeg, $"{mozjpeg_sw} -outfile {x.outf.WQ()} {x.inf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, x.inf, x.outf, ".jpg"), Interlocked.Increment(ref counter)))).ToArray();
                    break;
                case 1:// cwebp
                    tasks = GetFiles().Select(x => TaskAsync(cwebp, $"{cwebp_sw} {x.inf.WQ()} -o {x.outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, x.inf, x.outf, ".webp"), Interlocked.Increment(ref counter)))).ToArray();
                    break;
                case 2:// dwebp
                    tasks = GetFiles().Select(x => TaskAsync(dwebp, $"{dwebp_sw} {x.inf.WQ()} -o {x.outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, x.inf, x.outf, ".png"), Interlocked.Increment(ref counter)))).ToArray();
                    break;
                case 3:// manga
                    int gindex = 0;

                    tasks = GetFiles().Select(async x => {
                        Directory.CreateDirectory(x.outf);
                        await TaskAsyncMut(senvenzip, $"{senvenzip_sw} {x.inf.WQ()} -o{x.outf.WQ()}");
                        #region Ruduce Top Level
                        string topdir = x.outf;
                        while (Directory.EnumerateDirectories(topdir).Take(2).Count() == 1 && !Directory.EnumerateFiles(topdir).Any()) topdir += @"\" + Path.GetFileName(Directory.EnumerateDirectories(topdir).First());
                        #endregion
                        List<Task> optimizetasklist = new List<Task>();
                        foreach (string inf in Directory.EnumerateFiles(topdir, "*.*", SearchOption.AllDirectories)) {
                            string outf;
                            string ext = Path.GetExtension(inf);
                            if (exts[0].Contains(ext)) {
                                outf = Path.Combine(tmp_now, "g" + Interlocked.Increment(ref gindex).ToString());
                                optimizetasklist.Add(TaskAsync(mozjpeg, $"{mozjpeg_sw} -outfile {outf.WQ()} {inf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".jpg"), counter)));
                            } else if (exts[1].Contains(ext)) {
                                outf = Path.Combine(tmp_now, "g" + Interlocked.Increment(ref gindex).ToString());
                                optimizetasklist.Add(TaskAsync(cwebp, $"{cwebp_sw} {inf.WQ()} -o {outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".webp"), counter)));
                            }
                        }
                        await Task.WhenAll(optimizetasklist);
                        string outa = x.outf + ".rar";
                        await TaskAsyncMut(winrar, $"{winrar_sw} {outa.WQ()} {(topdir + @"\").WQ()}");
                        vm.Update(Replace(ref totaldelta, x.inf, outa, ".rar"), Interlocked.Increment(ref counter));
                        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                        tcs.SetResult(true);
                        return tcs.Task;
                    }).ToArray();
                    break;
            }
            if (!vm.Start(tasks.Length)) {
                SystemSounds.Asterisk.Play();
                return;
            }
            await Task.WhenAll(tasks);
            Directory.Delete(tmp_now, true);
            sw.Stop();
            TimeSpan ts = sw.Elapsed;
            SystemSounds.Asterisk.Play();
            MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
            vm.Idle.Value = true;
        }
    }
    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }

    public static class ProcessExtensions {
        public static Task WaitForExitAsync(this Process p) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            p.EnableRaisingEvents = true;
            p.Exited += (s, e) => {
                tcs.SetResult(true);
                p.Dispose();
            };
            p.Start();
            return tcs.Task;
        }
    }
}
