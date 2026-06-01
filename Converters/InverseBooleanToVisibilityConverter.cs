using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Photo_zip.Converters
{
    /// <summary>
    /// 布尔值反向可见转换器，用于空态和内容区互斥展示。
    /// </summary>
    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool flag && flag ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is Visibility visibility && visibility != Visibility.Visible;
        }
    }
}