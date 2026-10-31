using System.Windows.Controls;

namespace FolderShift.Views
{
    // Semua layar murni XAML yang terikat ke ViewModel; code-behind hanya memanggil InitializeComponent.
    public partial class MoveView : UserControl { public MoveView() => InitializeComponent(); }
    public partial class SelectStep : UserControl { public SelectStep() => InitializeComponent(); }
    public partial class CopyStep : UserControl { public CopyStep() => InitializeComponent(); }
    public partial class ReviewStep : UserControl { public ReviewStep() => InitializeComponent(); }
    public partial class DoneStep : UserControl { public DoneStep() => InitializeComponent(); }
    public partial class HistoryView : UserControl { public HistoryView() => InitializeComponent(); }
    public partial class SettingsView : UserControl { public SettingsView() => InitializeComponent(); }
    public partial class BusyOverlay : UserControl { public BusyOverlay() => InitializeComponent(); }
}
