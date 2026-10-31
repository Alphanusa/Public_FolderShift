using System.Reflection;
using System.Windows.Input;

namespace FolderShift
{
    public sealed class MainViewModel : Observable
    {
        object current;
        bool attentionDismissed;

        public MainViewModel()
        {
            Move = new MoveViewModel(() => IsHistoryPage = true);
            History = new HistoryViewModel();
            Settings = new SettingsViewModel();
            current = Move;
            OpenHistory = new Command(() => IsHistoryPage = true);
            DismissAttention = new Command(() => { attentionDismissed = true; Raise(nameof(ShowAttention)); });
            AppState.HistoryChanged += () => Raise(nameof(HistoryCount), nameof(ShowAttention), nameof(AttentionText));
        }

        public MoveViewModel Move { get; }
        public HistoryViewModel History { get; }
        public SettingsViewModel Settings { get; }
        public object Current => current;

        public ICommand OpenHistory { get; }
        public ICommand DismissAttention { get; }

        public bool IsMovePage { get => current == Move; set { if (value) Go(Move); } }
        public bool IsHistoryPage { get => current == History; set { if (value) Go(History); } }
        public bool IsSettingsPage { get => current == Settings; set { if (value) Go(Settings); } }

        public int HistoryCount => AppState.History.Count;
        public bool IsAdmin => AppState.IsAdmin;
        // InformationalVersion membawa akhiran pra-rilis (0.2.0-beta.1); bagian "+sha" dari SDK dibuang.
        public string Version => "v" + Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion.Split('+')[0];

        public bool ShowAttention => AppState.Settings.CheckOnOpen && !attentionDismissed && History.AttentionCount > 0 && current != History;
        public string AttentionText => History.AttentionCount + " tautan perlu perhatian. Aplikasi yang memakainya bisa tidak menemukan datanya.";

        void Go(object page)
        {
            current = page;
            if (page == History) History.Reload();
            Raise(nameof(Current), nameof(IsMovePage), nameof(IsHistoryPage), nameof(IsSettingsPage), nameof(ShowAttention));
        }
    }
}
