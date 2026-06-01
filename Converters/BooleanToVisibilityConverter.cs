using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Photo_zip.Converters
{
    /// <summary>
    /// 将布尔值转成可见状态，用于高级设置、忙碌状态等界面切换。
    /// </summary>
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility == Visibility.Visible;
        }
    }
}