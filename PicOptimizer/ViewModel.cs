using Reactive.Bindings;
using System;
using System.Linq;
using System.Reactive.Linq;

namespace PicOptimizer {
    public class ViewModel {
        int _total;
        public ReactivePropertySlim<int> Index { get; } = new ReactivePropertySlim<int>();
        public ReactivePropertySlim<double> Current { get; } = new ReactivePropertySlim<double>(-1);
        public ReactivePropertySlim<bool> Idle { get; } = new ReactivePropertySlim<bool>(true);
        public ReactivePropertySlim<double> Pvalue { get; } = new ReactivePropertySlim<double>();
        public ReactivePropertySlim<string> Ptext { get; } = new ReactivePropertySlim<string>();
        public ReactivePropertySlim<string> DeltaText { get; } = new ReactivePropertySlim<string>();
        public ViewModel() {
            Current.Subscribe(x => {
                Pvalue.Value = x / _total;
                Ptext.Value = $"{x} / {_total}";
            });
            Idle.Where(x => x).Subscribe(_ => {
                Pvalue.Value = 0;
                Ptext.Value = DeltaText.Value = null;
            });
        }
        readonly string[] SizeSuffixes = { "バイト", "KB", "MB", "GB" };
        string SizeSuffix(Int64 value, int decimalPlaces = 1) {
            if (decimalPlaces < 0) throw new ArgumentOutOfRangeException("小数位");
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

        public bool Start(int total) {
            if (total != 0) {
                _total = total;
                Current.Value = 0;
                return true;
            } else {
                Idle.Value = true;
                return false;
            }
        }
        public void Update(long totaldelta, int counter) {
            Current.Value = counter;
            DeltaText.Value = $"{SizeSuffix(totaldelta)} 減";
        }
    }
}
