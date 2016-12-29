using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LeapMotion
{
  /// <summary>
  /// Interaction logic for LeapInformer.xaml
  /// </summary>
  public partial class LeapInformer : UserControl, INotifyPropertyChanged
  {
    public LeapInformer()
    {
      InitializeComponent();
    }

    public event PropertyChangedEventHandler PropertyChanged;
  }
}
