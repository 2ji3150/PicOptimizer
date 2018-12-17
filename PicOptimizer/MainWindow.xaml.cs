using System;
using System.Collections.Concurrent;
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
            for (int i = 1; i <= Environment.ProcessorCount; i++) cb.Add(i);
        }
        const string cwebp = @"/C D:\FIONE\bin\libwebp\cwebp -quiet -mt -lossless -m 6 -q 100";
        const string dwebp = @"/C D:\FIONE\bin\libwebp\dwebp -quiet -mt";
        const string mozjpeg = @"/C D:\FIONE\bin\mozjpeg\jpegtran-static -copy none";
        const string unrar = "/C WinRAR x -ai -ibck", rar = "/C WinRAR a -m5 -md1024m -ep1 -r -ibck";
        readonly HashSet<string>[] exts = new HashSet<string>[] {
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bmp", ".png", ".tif", "tiff", ".webp" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".webp" },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" }
        };
        ConcurrentBag<int> cb = new ConcurrentBag<int>();
        string[] outext = new string[] { ".jpg", ".webp", ".png" };
        Func<string, string, string>[] GetArgs = new Func<string, string, string>[] {
            (inf,outf)=> $"{mozjpeg} -outfile {outf} {inf.WQ()}",
            (inf,outf)=>  $"{cwebp} {inf.WQ()} -o {outf}",
            (inf,outf)=> $"{dwebp} {inf.WQ()} -o {outf}"
        };
        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount);
        SemaphoreSlim mut = new SemaphoreSlim(1);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            ViewModel vm = (ViewModel)DataContext;
            vm.Idle.Value = false;
            sw.Restart();
            Task[] tasks;
            long totaldelta = 0;
            int counter = 0;
            string tmp_now = Path.Combine("TMP", DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss"));
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


            IEnumerable<string> GetFiles() {
                bool checkext(string file) => exts[vm.Index.Value].Contains(Path.GetExtension(file));
                foreach (string d in (string[])e.Data.GetData(DataFormats.FileDrop)) {
                    if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                        foreach (string f in Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).AsParallel().Where(f => checkext(f))) yield return f;
                    } else if (checkext(d)) yield return d;
                }
            }

            async Task<bool> TaskAsync(string arg) {
                await sem.WaitAsync();
                Process p = Process.Start(new ProcessStartInfo("cmd.exe", arg) { UseShellExecute = false, CreateNoWindow = true });
                p.EnableRaisingEvents = true;
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                p.Exited += (s, a) => {
                    sem.Release();
                    p.Dispose();
                    tcs.TrySetResult(true);
                };
                return await tcs.Task;
            }

            async Task<bool> TaskAsyncMut(string arg) {
                await mut.WaitAsync();
                Process p = Process.Start(new ProcessStartInfo("cmd.exe", arg) { UseShellExecute = false, CreateNoWindow = true });
                p.EnableRaisingEvents = true;
                TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                p.Exited += (s, a) => {
                    mut.Release();
                    p.Dispose();
                    tcs.TrySetResult(true);
                };
                return await tcs.Task;
            }

            #endregion

            switch (vm.Index.Value) {
                default:
                    tasks = GetFiles().Select(async f => {
                        await sem.WaitAsync();
                        cb.TryTake(out int num);
                        string outf = Path.Combine("TMP", num.ToString());
                        Process p = Process.Start(new ProcessStartInfo("cmd.exe", GetArgs[vm.Index.Value](f, outf)) { UseShellExecute = false, CreateNoWindow = true });
                        p.EnableRaisingEvents = true;
                        TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
                        p.Exited += (s, a) => {
                            vm.Update(Replace(ref totaldelta, f, outf, outext[vm.Index.Value]), Interlocked.Increment(ref counter));
                            cb.Add(num);
                            sem.Release();
                            p.Dispose();
                            tcs.TrySetResult(true);
                        };
                        await tcs.Task;
                    }).ToArray();
                    break;
                case 3:// manga
                    int gindex = 0;
                    tasks = GetFiles().Select(async f => {
                        int i = 0;
                        string outdir = Path.Combine("TMP", (++i).ToString());
                        string outrar = outdir + ".rar";
                        await TaskAsyncMut($"{unrar} {f.WQ()} {(outdir + @"\")}");
                        List<Task> optimizetasklist = new List<Task>();
                        foreach (string inf in Directory.EnumerateFiles(outdir, "*.*", SearchOption.AllDirectories)) {
                            string outf;
                            string ext = Path.GetExtension(inf);
                            if (exts[0].Contains(ext)) {
                                outf = Path.Combine(tmp_now, "g" + Interlocked.Increment(ref gindex).ToString());
                                optimizetasklist.Add(TaskAsync($"{mozjpeg} -outfile {outf.WQ()} {inf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".jpg"), counter)));
                            } else if (exts[1].Contains(ext)) {
                                outf = Path.Combine(tmp_now, "g" + Interlocked.Increment(ref gindex).ToString());
                                optimizetasklist.Add(TaskAsync($"{cwebp} {inf.WQ()} -o {outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".webp"), counter)));
                            }
                        }
                        await Task.WhenAll(optimizetasklist);
                        await TaskAsyncMut($"{rar} {outrar.WQ()} {(outdir + @"\")}");
                        vm.Update(Replace(ref totaldelta, f, outrar, ".rar"), Interlocked.Increment(ref counter));
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
}
