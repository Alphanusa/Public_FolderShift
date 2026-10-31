using System;
using System.Windows;

namespace FolderShift
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainViewModel();
            StateChanged += OnStateChanged;
        }

        // Dengan WindowChrome, jendela yang dimaksimalkan melewati tepi layar sebesar border resize.
        void OnStateChanged(object sender, EventArgs e)
        {
            var t = SystemParameters.WindowResizeBorderThickness;
            Root.Margin = WindowState == WindowState.Maximized
                ? new Thickness(t.Left + 4, t.Top + 4, t.Right + 4, t.Bottom + 4)
                : new Thickness(0);
        }

        void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
