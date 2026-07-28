using System.Windows;
using PictureSlideshowScreensaver.ViewModels;

namespace PictureSlideshowScreensaver
{
  /// <summary>
  /// Show-registry viewer, opened from the slideshow with the S key and
  /// closed with Escape. Mirrors LogViewer: all logic lives in the view
  /// model, the code-behind only wires the modal close request and the
  /// error MessageBox.
  /// </summary>
  public partial class StatsViewer : Window
  {
    public StatsViewer(StatsViewerViewModel vm)
    {
      DataContext = vm;
      vm.RequestClose += (_, _) => Close();
      vm.ShowError = msg => MessageBox.Show(this, msg, "Реестр показов", MessageBoxButton.OK, MessageBoxImage.Warning);
      InitializeComponent();
      vm.RefreshCommand.Execute(null);
    }
  }
}
