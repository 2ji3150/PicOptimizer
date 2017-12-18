using Reactive.Bindings;
using System.Linq;
using System.Reactive.Linq;
namespace PicOptimizer {
    public class ViewModel {
        public int total = 0;
        public ReactiveProperty<int> Index { get; } = new ReactiveProperty<int>();
        public ReactiveProperty<double> Current { get; } = new ReactiveProperty<double>();
        public ReactiveProperty<bool> Idle { get; } = new ReactiveProperty<bool>(true);
        public ReactiveProperty<double> Pvalue { get; } = new ReactiveProperty<double>();
        public ReactiveProperty<string> Ptext { get; } = new ReactiveProperty<string>();
        public ReactiveProperty<string> DeltaText { get; } = new ReactiveProperty<string>();
        public ViewModel() {
            Pvalue = Current.Select(x => total > 0 ? x / total : x).ToReactiveProperty();
            Ptext = Current.Select(x => total > 0 ? $"{x} / {total}" : null).ToReactiveProperty();
        }
    }
}
