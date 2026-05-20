using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PictureSlideshowScreensaver.ViewModels
{
  // INotifyPropertyChanged now comes from CommunityToolkit.Mvvm's
  // ObservableObject instead of a hand-rolled event. RaisePropertyChanged
  // is kept as a thin alias over OnPropertyChanged so existing setters
  // (and the nameof()-based notifications) keep working unchanged; new
  // code can also use SetProperty / [ObservableProperty] from the base.
  public class Notifier : ObservableObject
  {
    protected void RaisePropertyChanged([CallerMemberName] string caller_name = null)
        => OnPropertyChanged(caller_name);
  }

  public class BaseViewModel : Notifier
  {
  }
}
