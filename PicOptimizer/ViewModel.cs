using Reactive.Bindings;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;

namespace PicOptimizer {
    public class ViewModel {
        public int total = 0;
        int counter = 0;
        public long totaldelta = 0;
        public ReactiveProperty<int> Index { get; } = new ReactiveProperty<int>();
        public ReactiveProperty<double> Current { get; } = new ReactiveProperty<double>();
        public ReactiveProperty<bool> Idle { get; } = new ReactiveProperty<bool>(true);
        public ReactiveProperty<double> Pvalue { get; }
        public ReactiveProperty<string> Ptext { get; } = new ReactiveProperty<string>();
        public ReactiveProperty<string> DeltaText { get; } = new ReactiveProperty<string>();
        public ViewModel() {

            Pvalue = Current.Select(x => total > 0 ? x / total : 0).ToReactiveProperty();
            Ptext = Current.Select(x => total > 0 ? $"{x} / {total}" : null).ToReactiveProperty();
            DeltaText = Current.Select(x => total > 0 ? $"{SizeSuffix(totaldelta)} を減少した" : null).ToReactiveProperty();
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

        public void AddDelta(long delta) => Interlocked.Add(ref totaldelta, delta);
        public void IncrementCounter() => Current.Value = Interlocked.Increment(ref counter);
        public void Reset() {
            total = counter = 0;
            totaldelta = 0;
            Current.Value = 0;
        }
    }
}
