﻿using PixiEditor.Models.Enums;
using PixiEditor.Views;
using System;
using System.IO;
using System.Windows;

namespace PixiEditor.Models.Dialogs
{
    public static class ConfirmationDialog
    {
        public static ConfirmationType Show(string message)
        {
            ConfirmationPopup popup = new ConfirmationPopup
            {
                Body = message,
                Topmost = true
            };
            if (popup.ShowDialog().GetValueOrDefault())
            {
                return popup.Result ? ConfirmationType.Yes : ConfirmationType.No;
            }

            return ConfirmationType.Canceled;
        }
    }
}