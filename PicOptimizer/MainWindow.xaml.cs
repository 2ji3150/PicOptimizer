using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
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
        List<(string tempfile, string newfile, ProcessStartInfo psi, FileInfo fiI)> ProcessList = new List<(string tempfile, string newfile, ProcessStartInfo psi, FileInfo fiI)>();
        TimeSpan ts;
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Directory.CreateDirectory("GTEMP");
            ProcessList.Clear();
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
                    if (Directory.Exists("ATEMP")) Directory.Delete("ATEMP", true);
                    Directory.CreateDirectory("ATEMP");
                    files = GetFiles(new string[] { ".zip", ".rar", ".7z" }, dropdata);
                    vm.total = files.Count();
                    if (vm.total <= 0) return;
                    #region フェーズ１：展開
                    vm.DeltaText.Value = "フェーズ１：展開";
                    Directory.CreateDirectory("ATEMP");
                    List<(string orgarchive, string tempdir)> archivelist = new List<(string orgarchive, string tempdir)>();
                    int i = 0;
                    foreach (var f in files) {
                        var tempdir = Path.Combine("ATEMP", (++i).ToString());
                        Directory.CreateDirectory(tempdir);
                        archivelist.Add((f, tempdir));
                        Process.Start(Psi($@"/c tools\7z x {f.WQ()} -o{tempdir.WQ()}")).WaitForExit();
                        vm.Current.Value++;
                    }
                    vm.Reset();
                    #endregion
                    #region フェーズ２：画像圧縮

                    vm.DeltaText.Value = "フェーズ２：画像圧縮";

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
                    vm.Reset();
                    #endregion
                    #region フェーズ３：アーカイブ圧縮
                    vm.DeltaText.Value = "フェーズ３：アーカイブ圧縮";
                    vm.total = archivelist.Count();

                    foreach (var (orgarchive, tempdir) in archivelist) {
                        var temparchive = $"{tempdir}.rar";
                        try {
                            Process.Start(Psi($@"/c ""C:\Program Files\WinRAR\Rar.exe"" a -m5 -md1024m -ep1 -r {temparchive} {tempdir}\")).WaitForExit();
                            FileInfo fiI = new FileInfo(orgarchive), fiT = new FileInfo(temparchive);
                            if (fiT.Length > 0) {
                                var delta = fiI.Length - fiT.Length;
                                if (delta != 0) vm.AddDelta(delta);
                                fiI.IsReadOnly = false;
                                fiI.Delete();
                                fiT.MoveTo(Path.ChangeExtension(orgarchive, ".rar"));
                            }
                        } catch (Exception ex) {
                            MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {orgarchive}");
                        } finally {
                            vm.Current.Value++;
                        }
                    }
                    Directory.Delete("ATEMP", true);
                    #endregion
                    break;
            }
            sw.Stop();
            SystemSounds.Asterisk.Play();
            ts = sw.Elapsed;
            if (files.Count() != 0) MessageBox.Show($"完成しました\n\n処理にかかった時間 = {ts.Hours} 時間 {ts.Minutes} 分 {ts.Seconds} 秒 {ts.Milliseconds} ミリ秒");
            vm.Reset();
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


        Task Processing() => Task.Run(() => {
            vm.total = ProcessList.Count();
            if (vm.total <= 0) return;
            ProcessList.AsParallel().ForAll(p => {
                try {
                    Process.Start(p.psi).WaitForExit();
                    var fiT = new FileInfo(p.tempfile);
                    if (fiT.Length > 0) {
                        var delta = p.fiI.Length - fiT.Length;
                        if (delta != 0) vm.AddDelta(delta);
                        p.fiI.IsReadOnly = false;
                        p.fiI.Delete();
                        fiT.MoveTo(p.newfile);
                    }
                } catch (Exception ex) {
                    MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {p.fiI.Name}");
                } finally {
                    vm.IncrementCounter();
                }
            });
        });

        ProcessStartInfo Psi(string arg) => new ProcessStartInfo() { FileName = "cmd.exe", Arguments = arg, UseShellExecute = false, CreateNoWindow = true };

        string GetTempFilePath() => Path.Combine("GTEMP", Guid.NewGuid().ToString());
    }


    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }



}
