using System.Windows;
using PictureSlideshowScreensaver.ViewModels;

namespace PictureSlideshowScreensaver
{
  /// <summary>
  /// Tail viewer for the active Serilog file. Opened from the slideshow
  /// via the L key; closes on Escape. All file I/O and path resolution
  /// live in LogViewerViewModel — this code-behind only wires the modal
  /// close request and the error MessageBox callback.
  /// </summary>
  public partial class LogViewer : Window
  {
    public LogViewer(LogViewerViewModel vm)
    {
      DataContext = vm;
      vm.RequestClose += (_, _) => Close();
      vm.ShowError = msg => MessageBox.Show(this, msg, "Log viewer", MessageBoxButton.OK, MessageBoxImage.Warning);
      vm.ConfirmClear = () => MessageBox.Show(this, "Delete the current log file contents?", "Clear log",
                                              MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
      InitializeComponent();
      vm.RefreshCommand.Execute(null);
    }
  }
}
