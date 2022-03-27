﻿#nullable enable
using PixiEditor.Helpers;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PixiEditor.Models.DataHolders.Palettes
{
    public class Palette
    {
        public string Name { get; set; }
        public List<string> Colors { get; set; }
        public string FileName { get; set; }

        public bool IsFavourite { get; set; }

        public Palette(string name, List<string> colors, string fileName)
        {
            Name = name;
            Colors = colors;
            FileName = fileName;
        }
    }
}
