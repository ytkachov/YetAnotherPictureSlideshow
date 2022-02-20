using System.ComponentModel;

namespace PictureSlideshowScreensaver.ViewModels
{
  public class Notifier : INotifyPropertyChanged
  {
    #region Events

    public event PropertyChangedEventHandler PropertyChanged;
    protected void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string caller_name = null)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(caller_name));
    }

    #endregion
  }
  public class BaseViewModel : Notifier
  {
  }

}
