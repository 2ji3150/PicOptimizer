using Reactive.Bindings;
namespace PicOptimizer {
    public class ViewModel {
        public ReactiveProperty<int> Index { get; } = new ReactiveProperty<int>();
        public ReactiveProperty<bool> Idle { get; } = new ReactiveProperty<bool>(true);
        public ReactiveProperty<double> Pvalue { get; } = new ReactiveProperty<double>();
        public ReactiveProperty<string> Ptext { get; } = new ReactiveProperty<string>();
        public ReactiveProperty<string> DeltaText { get; } = new ReactiveProperty<string>();     
    }
}
