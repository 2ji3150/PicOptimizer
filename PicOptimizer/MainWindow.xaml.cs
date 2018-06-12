﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;

namespace PicOptimizer {
    public partial class MainWindow : Window {
        public MainWindow() => InitializeComponent();
        const string cwebp = @"tools\cwebp", cwebp_sw = "-quiet -mt -lossless -m 6 -q 100";
        const string dwebp = @"tools\dwebp", dwebp_sw = "-quiet -mt";
        const string mozjpeg = @"tools\jpegtran-static", mozjpeg_sw = "-copy all";
        const string winrar = @"tools\Rar", winrar_sw = "a -m5 -md1024m -ep1 -r -idq";
        const string senvenzip = @"tools\7z", senvenzip_sw = "x";
        readonly HashSet<string> ext_mozjpg = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };
        readonly HashSet<string> ext_cwebp = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".bmp", ".png", ".tif", "tiff", ".webp" };
        readonly HashSet<string> ext_dwebp = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".webp" };
        readonly HashSet<string> ext_archive = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };
        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount / 2);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            ViewModel vm = (ViewModel)DataContext;
            vm.Idle.Value = false;
            sw.Restart();
            string[] dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            Task[] tasks;
            List<Task> tasklist = new List<Task>();
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

            IEnumerable<string> GetFiles(HashSet<string> exts, string[] data) {
                foreach (string d in data) {
                    if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                        foreach (string f in Directory.EnumerateFiles(d, "*.*", SearchOption.AllDirectories).Where(f => exts.Contains(Path.GetExtension(f)))) yield return f;
                    } else if (exts.Contains(Path.GetExtension(d))) yield return d;
                }
            }

            async Task TaskAsync(string exe, string arg) {
                await sem.WaitAsync();
                try {
                    await RunProcessAsync(exe, arg);
                } finally {
                    sem.Release();
                }
            }
            #endregion

            int i = 0;
            var MozjpegAction = new TransformBlock<string,long>(async file => {
                string outf = Path.Combine(tmp_now, i++.ToString());
                await RunProcessAsync(mozjpeg, $"{mozjpeg_sw} -outfile {outf.WQ()} {file.WQ()}");
                FileInfo fiI = new FileInfo(file) { IsReadOnly = false }, fiT = new FileInfo(outf);
                if (fiT.Length <= 0) throw new Exception("生成したファイルが破損");
                fiI.Delete();
                fiT.MoveTo(Path.ChangeExtension(file, ".jpg"));
                return fiI.Length - fiT.Length;
            });

            var CwebpAction = new ActionBlock<string>(async file => {
                string outf = Path.Combine(tmp_now, i++.ToString());
                await RunProcessAsync(cwebp, $"{cwebp_sw} {file.WQ()} -o {outf.WQ()}");
            });

            var DwebpAction = new ActionBlock<string>(async file => {
                string outf = Path.Combine(tmp_now, i++.ToString());
                await RunProcessAsync(dwebp, $"{dwebp_sw} {file.WQ()} -o {outf.WQ()}");
            });

            var DecompressAction = new ActionBlock<string>(async file => {
                string tmp_a = Path.Combine(tmp_now, i++.ToString());
                DirectoryInfo di_a = Directory.CreateDirectory(tmp_a);
                await RunProcessAsync(senvenzip, $"{senvenzip_sw} {file} -o{di_a.FullName.WQ()}");
            });

            var CompressAction = new ActionBlock<string>(async file => {
                string tmp_a = Path.Combine(tmp_now, i++.ToString());
                DirectoryInfo di_a = Directory.CreateDirectory(tmp_a);
                string outa = tmp_a + ".rar";
                await RunProcessAsync(winrar, $"{winrar_sw} {outa.WQ()} {(topdir + @"\").WQ()}");
            });

            


            switch (vm.Index.Value) {
                default://MozJpeg
                    GetFiles(ext_mozjpg, dropdata).Select(f => MozjpegAction.Post(f));
                    MozjpegAction.Complete();
                    tasks = GetFiles(ext_mozjpg, dropdata).Select((item, index) => (item, index)).Select(x => {
                        string outf = Path.Combine(tmp_now, x.index.ToString());
                        return TaskAsync(mozjpeg, $"{mozjpeg_sw} -outfile {outf.WQ()} {x.item.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, x.item, outf, ".jpg"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    break;
                case 1:// cwebp
                    tasks = GetFiles(ext_cwebp, dropdata).Select((item, index) => (item, index)).Select(x => {
                        string outf = Path.Combine(tmp_now, x.index.ToString());
                        return TaskAsync(cwebp, $"{cwebp_sw} {x.item.WQ()} -o {outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, x.item, outf, ".webp"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    break;
                case 2:// dwebp
                    tasks = GetFiles(ext_dwebp, dropdata).Select((item, index) => (item, index)).Select(x => {
                        string outf = Path.Combine(tmp_now, x.index.ToString());
                        return TaskAsync(dwebp, $"{dwebp_sw} {x.item.WQ()} -o {outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, x.item, outf, ".png"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    break;
                case 3:// manga
                    tasks = GetFiles(ext_archive, dropdata).Select((item, index) => (item, index)).Select(x => {
                        string tmp_a = Path.Combine(tmp_now, x.index.ToString());
                        DirectoryInfo di_a = Directory.CreateDirectory(tmp_a);
                        return TaskAsync(senvenzip, $"{senvenzip_sw} {x.item.WQ()} -o{di_a.FullName.WQ()}").ContinueWith(async t => {
                            #region Ruduce Top Level
                            string topdir = tmp_a;
                            while (Directory.EnumerateDirectories(topdir).Take(2).Count() == 1 && !Directory.EnumerateFiles(topdir).Any()) topdir += @"\" + Path.GetFileName(Directory.EnumerateDirectories(topdir).First());
                            #endregion
                            List<Task> optimizetasklist = new List<Task>();
                            int gindex = 0;
                            foreach (string inf in Directory.EnumerateFiles(topdir, "*.*", SearchOption.AllDirectories)) {
                                string outf;
                                string ext = Path.GetExtension(inf);
                                if (ext_mozjpg.Contains(ext)) {
                                    outf = Path.Combine(tmp_a, (++gindex).ToString());
                                    optimizetasklist.Add(TaskAsync(mozjpeg, $"{mozjpeg_sw} -outfile {outf.WQ()} {inf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".jpg"), counter)));
                                } else if (ext_cwebp.Contains(ext)) {
                                    outf = Path.Combine(tmp_a, (++gindex).ToString());
                                    optimizetasklist.Add(TaskAsync(cwebp, $"{cwebp_sw} {inf.WQ()} -o {outf.WQ()}").ContinueWith(_ => vm.Update(Replace(ref totaldelta, inf, outf, ".webp"), counter)));
                                }
                            }
                            await Task.WhenAll(optimizetasklist);
                            string outa = tmp_a + ".rar";
                            await TaskAsync(winrar, $"{winrar_sw} {outa.WQ()} {(topdir + @"\").WQ()}");
                            vm.Update(Replace(ref totaldelta, x.item, outa, ".rar"), ++counter);
                            di_a.Delete(true);
                        }).Unwrap();
                    }).ToArray();
                    break;
            }
            if (!vm.Start(tasks.Length)) {
                SystemSounds.Asterisk.Play();
                return;
            }
            await Task.WhenAll(tasks);
            sw.Stop();
            TimeSpan ts = sw.Elapsed;
            SystemSounds.Asterisk.Play();
            MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
            vm.Idle.Value = true;
        }

#if DEBUG
        Task RunProcessAsync(string fn, string arg) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Process p = new Process {
                StartInfo = { FileName = fn, Arguments = arg, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true },
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
        Task RunProcessAsync(string fn, string arg) {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Process p = new Process {
                StartInfo = { FileName = fn, Arguments = arg, UseShellExecute = false, CreateNoWindow = true },
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
