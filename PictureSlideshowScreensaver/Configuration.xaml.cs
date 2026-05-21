using System;
using System.Windows;
using PictureSlideshowScreensaver.ViewModels;

namespace PictureSlideshowScreensaver
{
    /// <summary>
    /// Interaction logic for Configuration.xaml. State, validation and the
    /// Registry round-trip live in ConfigurationViewModel; the code-behind
    /// keeps only the inherently view-layer hooks — the OS folder picker,
    /// message boxes and closing the window.
    /// </summary>
    public partial class Configuration : Window
    {
        private readonly ConfigurationViewModel _vm;

        public Configuration(ConfigurationViewModel viewModel)
        {
            _vm = viewModel;
            _vm.BrowseForFolder = BrowseForFolder;
            _vm.ShowError = msg => MessageBox.Show(msg, "Error", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            _vm.RequestClose += (_, _) => Application.Current.Shutdown();

            DataContext = _vm;
            InitializeComponent();
        }

        private static string BrowseForFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_vm.HasUnsavedChanges &&
                MessageBox.Show("There are unsaved changes. Really exit the configuration?", "Unsaved changes", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                e.Cancel = true;
            }
        }
    }
}
