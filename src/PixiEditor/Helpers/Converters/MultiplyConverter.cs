﻿using System.Globalization;
using System.Windows;

namespace PixiEditor.Helpers.Converters;
internal class MultiplyConverter : SingleInstanceConverter<MultiplyConverter>
{
    public override object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double? actuallyValue = NumberToDouble(value);
        double? factor = NumberToDouble(parameter);
        if (actuallyValue is null || factor is null)
            return DependencyProperty.UnsetValue;
        return actuallyValue * factor;
    }

    public override object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return 0;
    }

    private double? NumberToDouble(object number)
    {
        return number switch
        {
            int n => n,
            uint n => n,
            float n => n,
            double n => n,
            short n => n,
            long n => n,
            ulong n => n,
            ushort n => n,
            _ => null,
        };
    }
}
