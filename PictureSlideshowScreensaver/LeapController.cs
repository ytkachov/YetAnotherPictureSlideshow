using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace LeapMotion
{

  internal interface ILeapEventDelegate
  {
    void LeapEventNotification(string EventName);
  }

  internal class LeapEventListener : Leap.Listener
  {
    ILeapEventDelegate eventDelegate;

    public LeapEventListener(ILeapEventDelegate delegateObject)
    {
      this.eventDelegate = delegateObject;
    }

    public override void OnInit(Leap.Controller controller)
    {
      this.eventDelegate.LeapEventNotification("onInit");
    }
    public override void OnConnect(Leap.Controller controller)
    {
      controller.SetPolicy(Leap.Controller.PolicyFlag.POLICY_IMAGES);
      controller.EnableGesture(Leap.Gesture.GestureType.TYPE_SWIPE);
      this.eventDelegate.LeapEventNotification("onConnect");
    }

    public override void OnFrame(Leap.Controller controller)
    {
      this.eventDelegate.LeapEventNotification("onFrame");
    }
    public override void OnExit(Leap.Controller controller)
    {
      this.eventDelegate.LeapEventNotification("onExit");
    }
    public override void OnDisconnect(Leap.Controller controller)
    {
      this.eventDelegate.LeapEventNotification("onDisconnect");
    }
  }

  public class LeapController : Leap.Listener
  {
    bool _isClosing = false;
    private Leap.Controller _controller = new Leap.Controller();

    public LeapController()
    {
      _controller.AddListener(this);
    }

    delegate void LeapEventDelegate(string EventName);
    public void LeapEventNotification(string EventName)
    {
      if (this.CheckAccess())
      {
        switch (EventName)
        {
          case "onInit":
            break;

          case "onConnect":
            connectHandler();
            break;

          case "onFrame":
            if (!_isClosing)
              newFrameHandler(_controller.Frame());

            break;
        }
      }
      else
      {
        Dispatcher.Invoke(new LeapEventDelegate(LeapEventNotification), new object[] { EventName });
      }
    }

    void connectHandler()
    {
      _controller.SetPolicy(Leap.Controller.PolicyFlag.POLICY_DEFAULT);
      _controller.EnableGesture(Leap.Gesture.GestureType.TYPE_SWIPE);
      _controller.Config.SetFloat("Gesture.Swipe.MinLength", 100.0f);
    }

    void newFrameHandler(Leap.Frame frame)
    {
      string stat = frame.Id.ToString() + " TM:" +
                    frame.Timestamp.ToString() + " FPS:" +
                    frame.CurrentFramesPerSecond.ToString() + " VLD:" +
                    frame.IsValid.ToString() + " GC:" +
                    frame.Gestures().Count.ToString();

      PhotoProperties.Photo_Description = stat;
    }


  }
}