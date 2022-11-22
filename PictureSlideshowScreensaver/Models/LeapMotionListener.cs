using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

using Leap;

namespace LeapMotion
{

  public class LeapMotionListener : Listener
  {
    private Controller _controller = new Controller();
    private bool _connected;
    private bool _present;

    public bool connected
    {
      get
      {
        return _connected;
      }
    }

    public bool present
    {
      get
      {
        return _present;
      }
    }


    public LeapMotionListener()
    {
      _controller.AddListener(this);
    }

    public override void OnInit(Controller controller)
    {
      _present = true;
    }

    public override void OnConnect(Controller controller)
    {
      _connected = true;

      controller.SetPolicy(Controller.PolicyFlag.POLICY_DEFAULT);
      controller.EnableGesture(Gesture.GestureType.TYPE_CIRCLE);
      controller.EnableGesture(Gesture.GestureType.TYPE_KEY_TAP);
      controller.EnableGesture(Gesture.GestureType.TYPE_SCREEN_TAP);

      controller.EnableGesture(Gesture.GestureType.TYPE_SWIPE);
      controller.Config.SetFloat("Gesture.Swipe.MinLength", 100.0f);
    }

    public override void OnDisconnect(Controller controller)
    {
      _connected = true;
    }

    public override void OnExit(Controller controller)
    {
      _present = false;
    }

    public void Close()
    {
      _controller.RemoveListener(this);
      _controller.Dispose();
    }
  }
}