using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PicOptimizer {
    public partial class MainWindow : Window {
        public MainWindow() => InitializeComponent();
        const string cwebp = @"tools\cwebp", cwebp_sw = "-quiet -mt -lossless -m 6 -q 100";
        const string dwebp = @"tools\dwebp", dwebp_sw = "-quiet -mt";
        const string mozjpeg = @"tools\jpegtran-static", mozjpeg_sw = "-copy all";
        const string winrar = @"C:\Program Files\WinRAR\Rar", winrar_sw = "a -m5 -md1024m -ep1 -r -idq";
        const string senvenzip = @"tools\7z", senvenzip_sw = "x";
        readonly string[] jpg_ext = new string[] { ".jpg", ".jpeg" };
        readonly string[] losslessimg_ext = new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" };
        readonly string[] webp_ext = new string[] { ".webp" };
        readonly string[] archive_ext = new string[] { ".zip", ".rar", ".7z" };

        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            ViewModel vm = (ViewModel)DataContext;
            vm.Idle.Value = false;
            sw.Restart();
            string[] dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            Task[] tasks;
            long totaldelta = 0;
            int counter = 0, index = 0;

            string time = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            string tmp_g = Path.Combine("TEMP", time, "G"), tmp_a = Path.Combine("TEMP", time, "A");
            Directory.CreateDirectory(tmp_g);

            #region ローカル関数
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

            IEnumerable<string> GetFiles(string[] exts, string[] data) {
                foreach (string d in data) {
                    if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                        foreach (string f in Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).Where(f => exts.Contains(Path.GetExtension(f).ToLower()))) yield return f;
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
            #endregion

            switch (vm.Index.Value) {
                default://MozJpeg
                    tasks = GetFiles(jpg_ext, dropdata).Select(inf => {
                        string outf = GetTempFilePath(ref index);
                        return TaskAsync(mozjpeg, new string[] { mozjpeg_sw, "-outfile", outf.WQ(), inf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".jpg"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) {
                        SystemSounds.Asterisk.Play();
                        return;
                    }
                    await Task.WhenAll(tasks);
                    break;
                case 1:// cwebp
                    tasks = GetFiles(losslessimg_ext, dropdata).Select(inf => {
                        string outf = GetTempFilePath(ref index);
                        return TaskAsync(cwebp, new string[] { cwebp_sw, inf.WQ(), "-o", outf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".webp"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) {
                        SystemSounds.Asterisk.Play();
                        return;
                    }
                    await Task.WhenAll(tasks);
                    break;
                case 2:// dwebp
                    tasks = GetFiles(webp_ext, dropdata).Select(inf => {
                        string outf = GetTempFilePath(ref index);
                        return TaskAsync(dwebp, new string[] { dwebp_sw, inf.WQ(), "-o", outf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".png"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) {
                        SystemSounds.Asterisk.Play();
                        return;
                    }
                    await Task.WhenAll(tasks);
                    break;
                case 3:// manga
                    string[] dropfiles = GetFiles(archive_ext, dropdata).ToArray();
                    if (!vm.Start(dropfiles.Length)) {
                        SystemSounds.Asterisk.Play();
                        return;
                    }
                    DirectoryInfo di = new DirectoryInfo(tmp_a);

                    foreach (string a in dropfiles) {
                        di.Create();
                        await RunProcessAsync(senvenzip, new string[] { senvenzip_sw, a.WQ(), "-o" + tmp_a.WQ() });
                        #region Ruduce Top Level
                        string topdir = tmp_a;
                        while (Directory.EnumerateDirectories(topdir).Take(2).Count() == 1 && !Directory.EnumerateFiles(topdir).Any()) topdir += @"\" + Path.GetFileName(Directory.EnumerateDirectories(topdir).First());

                        #endregion

                        List<Task> optimizetasklist = new List<Task>();
                        long tempdelta = totaldelta;

                        foreach (string inf in Directory.EnumerateFiles(topdir, "*.*", SearchOption.AllDirectories)) {
                            string outf;
                            string ext = Path.GetExtension(inf).ToLower();
                            if (jpg_ext.Contains(ext)) {
                                outf = GetTempFilePath(ref index);
                                optimizetasklist.Add(TaskAsync(mozjpeg, new string[] { mozjpeg_sw, "-outfile", outf.WQ(), inf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref tempdelta, inf, outf, ".jpg"), counter)));
                            } else if (losslessimg_ext.Contains(ext)) {
                                outf = GetTempFilePath(ref index);
                                optimizetasklist.Add(TaskAsync(cwebp, new string[] { cwebp_sw, inf.WQ(), "-o", outf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref tempdelta, inf, outf, ".webp"), counter)));
                            }
                        }
                        await Task.WhenAll(optimizetasklist);
                        string outa = tmp_a + ".rar";
                        await RunProcessAsync(winrar.WQ(), new string[] { winrar_sw, outa.WQ(), $@"{topdir}\".WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, a, outa, ".rar"), ++counter));
                        di.Delete(true);
                        di.Refresh();
                    }
                    break;
            }
            sw.Stop();
            TimeSpan ts = sw.Elapsed;
            SystemSounds.Asterisk.Play();
            MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
            vm.Idle.Value = true;
        }





#if DEBUG
        Task RunProcessAsync(string filename, string[] arguments) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Process p = new Process {
                StartInfo = { FileName = filename, Arguments = string.Join(" ", arguments), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true },
                EnableRaisingEvents = true
            };
            StringBuilder stdout = new StringBuilder(), stderr = new StringBuilder();
            p.OutputDataReceived += (s, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            p.Exited += (s, e) => {
                tcs.SetResult(true);
                p.Dispose();
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            return tcs.Task;
        }
#else
        Task RunProcessAsync(string filename, string[] arguments) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Process p = new Process {
                StartInfo = { FileName = filename, Arguments = string.Join(" ", arguments), UseShellExecute = false, CreateNoWindow = true },
                EnableRaisingEvents = true
            };
            p.Exited += (s, e) => {
                tcs.SetResult(true);
                p.Dispose();
            };
            p.Start();
            return tcs.Task;
        }
#endif

    }
    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }
}
