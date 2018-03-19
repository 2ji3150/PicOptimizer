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

        const string enwebp = @"/c tools\cwebp -quiet -lossless -z 9 -mt";
        const string unwebp = @"/c tools\dwebp -mt";
        const string mozjpeg = @"/c tools\jpegtran-static -copy all";
        const string mozjpeg_arg2 = ">";
        const string webparg2 = "-o";
        List<(string tempfile, string newfile, ProcessStartInfo psi, FileInfo fiI)> ProcessList = new List<(string tempfile, string newfile, ProcessStartInfo psi, FileInfo fiI)>();
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            vm.Idle.Value = false;
            var dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            IEnumerable<string> files;
            switch (vm.Index.Value) {
                default://MozJpeg
                    files = GetFiles(new string[] { ".jpg", ".jpeg" }, dropdata);
                    foreach (var f in files) {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".jpg");
                        ProcessList.Add((tempf, newf, Psi($"{mozjpeg} {f.WQ()} > {tempf.WQ()}"), new FileInfo(f)));
                    }
                    await Processing();
                    break;
                case 1:// Webp Lossless
                    files = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, dropdata);
                    foreach (var f in files) {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".webp");
                        ProcessList.Add((tempf, newf, Psi($"{enwebp} {f.WQ()} -o {tempf.WQ()}"), new FileInfo(f)));
                    }
                    await Processing();
                    break;
                case 2:// Decode Webp
                    files = GetFiles(new string[] { ".webp" }, dropdata);
                    foreach (var f in files) {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(f, ".png");
                        ProcessList.Add((tempf, newf, Psi($"{unwebp} {f.WQ()} -o {tempf.WQ()}"), new FileInfo(f)));
                    }
                    await Processing();
                    break;
                case 3:// manga
                    files = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata);
                    vm.total = files.Count();
                    if (vm.total <= 0) return;
                    vm.DeltaText.Value = "フェーズ１：展開";
                    List<(string orgarchive, string tempdir)> archivelist = new List<(string orgarchive, string tempdir)>();
                    foreach (var archive in files) {
                        var tempdir = Path.Combine("ATEMP", $"{Guid.NewGuid()}");
                        archivelist.Add((archive, tempdir));
                        Process.Start(Psi($@"/c tools\7z x {archive.WQ()} -o {tempdir.WQ()}")).WaitForExit();
                    }

                    var jpgfiles = GetFiles(new string[] { ".jpg", ".jpeg" }, new string[] { "ATEMP" });
                    foreach (var jf in jpgfiles) {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(jf, ".jpg");
                        ProcessList.Add((tempf, newf, Psi($"{mozjpeg} {jf.WQ()} > {tempf.WQ()}"), new FileInfo(jf)));
                    }
                    var losslessfiles = GetFiles(new string[] { ".bmp", ".png", ".tif", "tiff", ".webp" }, new string[] { "ATEMP" });
                    foreach (var lf in losslessfiles) {
                        var tempf = GetTempFilePath();
                        var newf = Path.ChangeExtension(lf, ".webp");
                        ProcessList.Add((tempf, newf, Psi($"{enwebp} {lf.WQ()} -o {tempf.WQ()}"), new FileInfo(lf)));
                    }
                    await Processing();

                    break;
            }


            vm.Idle.Value = true;
        }

        IEnumerable<string> GetFiles(string[] exts, string[] data) {
            foreach (var d in data) {
                if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                    foreach (var f in exts.AsParallel().SelectMany(sp => Directory.EnumerateFiles(d, $"*{sp}", SearchOption.AllDirectories))) {
                        yield return f;
                    }
                } else if (exts.Contains(Path.GetExtension(d).ToLower())) yield return d;
            }
        }

        Task Processing(IEnumerable<string> files, string arg1, string arg2, string ext) => Task.Run(() => {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            try {
                vm.DeltaText.Value = null;
                int counter = 0;
                files.AsParallel().ForAll(f => {
                    try {
                        var tempf = Path.Combine(Path.GetDirectoryName(f), $"{Guid.NewGuid()}");
                        Process.Start(Psi($"{arg1} {f.WQ()} {arg2} {tempf.WQ()}")).WaitForExit();
                        FileInfo fiI = new FileInfo(f), fiT = new FileInfo(tempf);
                        if (fiT.Length > 0) {
                            var delta = fiI.Length - fiT.Length;
                            if (delta != 0) vm.AddDelta(delta);
                            fiI.IsReadOnly = false;
                            fiI.Delete();
                            fiT.MoveTo(Path.ChangeExtension(f, ext));
                            vm.Current.Value = Interlocked.Increment(ref counter);
                        } else {
                            MessageBox.Show($"error on: {fiI.Name}");
                            fiT.Delete();
                        }

                    } catch (Exception ex) {
                        MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {f}");
                    }
                });
            } catch (Exception ex) {
                MessageBox.Show(ex.Message);
            } finally {
                sw.Stop();
                SystemSounds.Asterisk.Play();
                MessageBox.Show($"完成しました\n\nミリ秒単位の経過時間の合計 ={sw.ElapsedMilliseconds}");
                vm.Reset();

            }
        });

        Task Processing() => Task.Run(() => {
            vm.total = ProcessList.Count();
            if (vm.total <= 0) return;
            Parallel.ForEach(ProcessList, p => {
                try {
                    Process.Start(p.psi).WaitForExit();
                    var fiT = new FileInfo(p.tempfile);
                    if (fiT.Length > 0) {
                        var delta = p.fiI.Length - fiT.Length;
                        if (delta != 0) vm.AddDelta(delta);
                        p.fiI.IsReadOnly = false;
                        p.fiI.Delete();
                        fiT.MoveTo(p.newfile);
                        vm.IncrementCounter();

                    }
                } catch (Exception ex) {
                    MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {p.fiI.Name}");
                }
            });
        });

        ProcessStartInfo Psi(string arg) => new ProcessStartInfo() { FileName = "cmd.exe", Arguments = arg, UseShellExecute = false, CreateNoWindow = true };


        string GetTempFilePath() => Path.Combine("GTEMP", $"{Guid.NewGuid()}");
    }


    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }



}
