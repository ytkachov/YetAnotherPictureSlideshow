using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace PictureSlideshowScreensaver.ViewModels
{
  internal sealed class BooleanToVisibilityConverter : IValueConverter
  {
    public bool Inverse { get; set; }
    public bool Collapse { get; set; }
    public BooleanToVisibilityConverter() { Collapse = true; Inverse = false; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (targetType != typeof(Visibility))
        throw new InvalidOperationException("The target must be of type System.Windows.Visibility");

      var b = false;
      if (value is bool)
      {
        b = System.Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        b = Inverse ? !b : b;
      }

      return b ? Visibility.Visible : (Collapse ? Visibility.Collapsed : Visibility.Hidden);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      return Binding.DoNothing;
    }
  }
}
