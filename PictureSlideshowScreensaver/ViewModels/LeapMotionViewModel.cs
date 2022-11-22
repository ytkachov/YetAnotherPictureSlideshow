using System;
using System.ComponentModel;
using System.Windows.Threading;

using LeapMotion;

namespace informers
{
  public enum Fingers
  {
    None,
    RightFive,
    RightFiveSpread
  }

  public class LeapMotionInformer : INotifyPropertyChanged
  {
    private DispatcherTimer _lm_Tick = new DispatcherTimer();
    private Fingers _hand_config;

    public Fingers HandConfig
    {
      get { return _hand_config; }
      set { _hand_config = value; RaisePropertyChanged(); }
    }

    public LeapMotionInformer()
    {
      _lm_Tick.Tick += new EventHandler(controller_Tick);
      _lm_Tick.Interval = TimeSpan.FromMilliseconds(50.0);
      _lm_Tick.Start();
    }

    void controller_Tick(object sender, EventArgs e)
    {
    }

    public void Close()
    {
    }

    // INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    private void RaisePropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string caller_name = null)
    {
      if (PropertyChanged != null)
        PropertyChanged(this, new PropertyChangedEventArgs(caller_name));
    }
  }
}
