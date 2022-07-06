﻿using System.ComponentModel;

namespace PixiEditor.Helpers.Extensions;

internal static class EnumHelpers
{
    public static IEnumerable<T> GetFlags<T>(this T e)
        where T : Enum
    {
        return Enum.GetValues(e.GetType()).Cast<T>().Where(x => e.HasFlag(x));
    }

    public static string GetDescription<T>(this T enumValue)
        where T : struct, Enum
    {
        var description = enumValue.ToString();
        var fieldInfo = enumValue.GetType().GetField(enumValue.ToString());

        if (fieldInfo != null)
        {
            var attrs = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), true);
            if (attrs != null && attrs.Length > 0)
            {
                description = ((DescriptionAttribute)attrs[0]).Description;
            }
        }

        return description;
    }
}