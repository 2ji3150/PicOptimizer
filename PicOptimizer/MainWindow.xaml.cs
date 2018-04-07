﻿using System;
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
        const string winrar = @"""%ProgramFiles%\WinRAR\Rar""", winrar_sw = "a -m5 -md1024m -ep1 -r";
        const string senvenzip = @"tools\7z", senvenzip_sw = "x";
        const string tempdir_g = @"TEMP\G";
        const string tempdir_a = @"TEMP\A";

        SemaphoreSlim sem = new SemaphoreSlim(Environment.ProcessorCount - 2);
        Stopwatch sw = new Stopwatch();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            vm.Idle.Value = false;
            sw.Restart();
            Directory.CreateDirectory(tempdir_g);
            var dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            Task[] tasks;
            long totaldelta = 0;
            int counter = 0, index = 0;

            string GetTempFilePath(ref int idnum) => Path.Combine(tempdir_g, (++idnum).ToString());

            long Replace(ref long td, string file, string tempfile, string ext) {
                try {
                    FileInfo fiI = new FileInfo(file) { IsReadOnly = false }, fiT = new FileInfo(tempfile);
                    if (fiT.Length <= 0) throw new Exception("生成したファイルが破損");
                    var delta = fiI.Length - fiT.Length;
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
                    tasks = GetFiles(new string[] { ".jpg", ".jpeg" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath(ref index);
                        return TaskAsync(mozjpeg, new string[] { "-copy all", f.WQ(), ">", tempf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, f, tempf, ".jpg"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(index)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 1:// cwebp
                    tasks = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath(ref index);
                        return TaskAsync(cwebp, new string[] { cwebp_sw, f.WQ(), "-o", tempf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, f, tempf, ".webp"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(index)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 2:// dwebp
                    tasks = GetFiles(new string[] { ".webp" }, dropdata).Select(f => {
                        var tempf = GetTempFilePath(ref index);
                        return TaskAsync(dwebp, new string[] { dwebp_sw, f.WQ(), "-o", tempf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, f, tempf, ".png"), Interlocked.Increment(ref counter)));
                    }).ToArray();
                    if (!vm.Start(tasks.Length)) return;
                    await Task.WhenAll(tasks);
                    break;
                case 3:// manga
                    var dropfiles = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata).ToArray();
                    if (!vm.Start(dropfiles.Length)) return;
                    DirectoryInfo di = new DirectoryInfo(tempdir_a);
                    if (di.Exists) di.Delete(true);
                    foreach (var a in dropfiles) {
                        di.Create();
                        await RunProcessAsync(senvenzip, new string[] { senvenzip_sw, a.WQ(), $"-o{tempdir_a}" });

                        #region Ruduce Dir Level
                        var tempdir = tempdir_a;
                        DirectoryInfo ditemp = di;
                        while (!ditemp.EnumerateFiles().Any() && ditemp.EnumerateDirectories().Count() == 1) {
                            tempdir += @"\" + ditemp.EnumerateDirectories().ToArray()[0].Name;
                            ditemp = new DirectoryInfo(tempdir);
                        }
                        #endregion

                        var optimizetasklist = new List<Task>();
                        long tempdelta = totaldelta;

                        foreach (string f in Directory.EnumerateFiles(tempdir, "*.*", SearchOption.AllDirectories)) {
                            string tempf;
                            string ext = Path.GetExtension(f).ToLower();
                            if (new string[] { ".jpg", ".jpeg" }.Contains(ext)) {
                                tempf = GetTempFilePath(ref index);
                                optimizetasklist.Add(TaskAsync(mozjpeg, new string[] { mozjpeg_sw, f.WQ(), ">", tempf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref tempdelta, f, tempf, ".jpg"), counter)));
                            } else if (new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }.Contains(ext)) {
                                tempf = GetTempFilePath(ref index);
                                optimizetasklist.Add(TaskAsync(cwebp, new string[] { cwebp_sw, f.WQ(), "-o", tempf.WQ() }).ContinueWith(_ => vm.Update(Replace(ref tempdelta, f, tempf, ".webp"), counter)));
                            }
                        }

                        await Task.WhenAll(optimizetasklist);
                        await RunProcessAsync(winrar, new string[] { winrar_sw, tempdir.WQ(), $@"{tempdir}\".WQ() }).ContinueWith(_ => vm.Update(Replace(ref totaldelta, a, $"{tempdir}.rar", ".rar"), ++counter));
                        di.Delete();
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

        async Task TaskAsync(string exe, string[] arg) {
            await sem.WaitAsync();
            try {
                await RunProcessAsync(exe, arg);
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
