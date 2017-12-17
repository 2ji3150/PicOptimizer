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

        const string webpencode_arg = @"/c tools\cwebp -quiet -lossless -z 9";
        const string webpdecode_arg = @"/c tools\dwebp";
        const string mozjpeg_arg1 = @"/c tools\jpegtran -copy all";
        const string mozjpeg_arg2 = ">";
        const string webparg2 = "-o";
        readonly string[] searchpattern = new string[] { "*.bmp", "*.png", "*.tif", "*.webp" };
        readonly string[] exts = new string[] { ".bmp", ".png", ".tif", ".webp" };
        long TotalDelta = 0;
        private void Window_DragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        private async void Window_Drop(object sender, DragEventArgs e) {
            vm.Idle.Value = false;
            var dropdata = (string[])e.Data.GetData(DataFormats.FileDrop);
            IEnumerable<string> files;
            string arg1, arg2, ext;
            switch (vm.Index.Value) {
                default://MozJpeg
                    files = GetFiles(dropdata, ".jpg");
                    arg1 = mozjpeg_arg1;
                    arg2 = mozjpeg_arg2;
                    ext = ".jpg";
                    break;
                case 1:// Webp
                    files = GetFilesForWebp(dropdata);
                    arg1 = webpencode_arg;
                    arg2 = webparg2;
                    ext = ".webp";
                    break;
                case 2:// Decode
                    files = GetFiles(dropdata, ".webp");
                    arg1 = webpdecode_arg;
                    arg2 = webparg2;
                    ext = ".png";
                    break;
            }
            vm.total = files.Count();
            if (vm.total > 0) await Processing(files, arg1, arg2, ext);
            else {
                SystemSounds.Asterisk.Play();
                MessageBox.Show("何も処理されません...");
            }
            vm.Idle.Value = true;
        }



        IEnumerable<string> GetFiles(string[] data, string ext) {
            foreach (var d in data) {
                if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                    foreach (var f in Directory.EnumerateFiles(d, $"*{ext}", SearchOption.AllDirectories)) {
                        yield return f;
                    }
                }
                else if (d.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) yield return d;
            }
        }

        IEnumerable<string> GetFilesForWebp(string[] data) {
            foreach (var d in data) {
                if (File.GetAttributes(d).HasFlag(FileAttributes.Directory)) {
                    foreach (var f in searchpattern.AsParallel().SelectMany(sp => Directory.EnumerateFiles(d, sp))) {
                        yield return f;
                    }
                }
                else if (exts.Contains(Path.GetExtension(d).ToLower())) yield return d;
            }
        }




        Task Processing(IEnumerable<string> files, string arg1, string arg2, string ext) => Task.Run(() => {
            try {
                vm.DeltaText.Value = null;
                vm.Ptext.Value = $"0 / {vm.total}";
                int counter = 0;
                files.AsParallel().ForAll(f => {
                    try {
                        var tempf = Path.Combine(Path.GetDirectoryName(f), $"{Guid.NewGuid()}");
                        Process.Start(Psi($"{arg1} {f.WQ()} {arg2} {tempf.WQ()}")).WaitForExit();
                        FileInfo fiI = new FileInfo(f), fiT = new FileInfo(tempf);
                        if (fiT.Length > 0) {
                            var delta = fiI.Length - fiT.Length;
                            if (delta != 0) Interlocked.Add(ref TotalDelta, fiI.Length - fiT.Length);
                            vm.DeltaText.Value = $"{SizeSuffix(TotalDelta)} を減少した";
                            fiI.IsReadOnly = false;
                            fiI.Delete();
                            fiT.MoveTo(Path.ChangeExtension(f, ext));
                            vm.Current.Value = Interlocked.Increment(ref counter);
                        }
                        else {
                            MessageBox.Show($"error on: {f}");
                            fiT.Delete();
                        }

                    }
                    catch (Exception ex) {
                        MessageBox.Show($"{ex.Message}{Environment.NewLine}on: {f}");
                    }
                });
            }
            catch (Exception ex) {
                MessageBox.Show(ex.Message);
            }
            finally {
                SystemSounds.Asterisk.Play();
                MessageBox.Show("完成しました");

                vm.total = 0;
                TotalDelta = 0;
                vm.Current.Value = 0;
                vm.Ptext.Value = null;
                vm.Idle.Value = true;
            }
        });

        ProcessStartInfo Psi(string arg) => new ProcessStartInfo() { FileName = "cmd.exe", Arguments = arg, UseShellExecute = false, CreateNoWindow = true };



        readonly string[] SizeSuffixes = { "バイト", "KB", "MB", "GB", "TB" };
        public string SizeSuffix(Int64 value, int decimalPlaces = 1) {
            if (decimalPlaces < 0) throw new ArgumentOutOfRangeException("decimalPlaces");
            if (value < 0) return $"-{SizeSuffix(-value)}";
            if (value == 0) return "0 バイト";
            int mag = (int)Math.Log(value, 1024);
            decimal adjustedSize = (decimal)value / (1L << (mag * 10));
            if (Math.Round(adjustedSize, decimalPlaces) >= 1000) {
                mag += 1;
                adjustedSize /= 1024;
            }
            Console.WriteLine(adjustedSize.ToString());
            return $"{adjustedSize:n}{decimalPlaces} {SizeSuffixes[mag]}";
        }
    }

    public static class StringExtensionMethods {
        public static string WQ(this string text) => $@"""{text}""";
    }


}
