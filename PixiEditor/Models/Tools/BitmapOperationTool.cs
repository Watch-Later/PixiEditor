﻿using System;
using System.Collections.Generic;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using PixiEditor.Models.DataHolders;
using PixiEditor.Models.Layers;
using PixiEditor.Models.Position;
using PixiEditor.Models.Undo;
using SkiaSharp;

namespace PixiEditor.Models.Tools
{
    public abstract class BitmapOperationTool : Tool
    {
        public bool RequiresPreviewLayer { get; set; }

        public bool ClearPreviewLayerOnEachIteration { get; set; } = true;

        public bool UseDefaultUndoMethod { get; set; } = true;
        public virtual bool UsesShift => true;

        private StorageBasedChange _change;

        public abstract void Use(Layer layer, List<Coordinates> mouseMove, SKColor color);

        /// <summary>
        /// Executes undo adding procedure.
        /// </summary>
        /// <param name="document">Active document</param>
        /// <remarks>When overriding, set UseDefaultUndoMethod to false.</remarks>
        public override void AddUndoProcess(Document document)
        {
            if (!UseDefaultUndoMethod) return;

            var args = new object[] { _change.Document };
            document.UndoManager.AddUndoChange(_change.ToChange(StorageBasedChange.BasicUndoProcess, args));
            _change = null;
        }

        public override void OnRecordingLeftMouseDown(MouseEventArgs e)
        {
            if (UseDefaultUndoMethod && e.LeftButton == MouseButtonState.Pressed)
            {
                Document doc = ViewModels.ViewModelMain.Current.BitmapManager.ActiveDocument;
                _change = new StorageBasedChange(doc, new[] { doc.ActiveLayer }, true);
            }
        }
    }
}
