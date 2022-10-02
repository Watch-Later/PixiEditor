﻿using PixiEditor.DrawingApi.Core.ColorsImpl;
using PixiEditor.DrawingApi.Core.Numerics;
using PixiEditor.DrawingApi.Core.Surface;
using SkiaSharp;

namespace PixiEditor.DrawingApi.Skia
{
    public static class ConversionExtensions
    {
        public static SKRect ToSKRect(this RectD rectD)
        {
            return SKRect.Create((float)rectD.X, (float)rectD.Y, (float)rectD.Width, (float)rectD.Height);
        }

        public static SKColor ToSKColor(this Color color)
        {
            return new SKColor(color.R, color.G, color.B, color.A);
        }

        public static SKMatrix ToSkMatrix(this Matrix3X3 matrix)
        {
            return new SKMatrix(matrix.ScaleX, matrix.SkewX, matrix.TransX, matrix.SkewY, 
                matrix.ScaleY, matrix.TransY, matrix.Persp0, matrix.Persp1, matrix.Persp2);
        }
        
        public static SKPoint ToSkPoint(this Point vector)
        {
            return new SKPoint(vector.X, vector.Y);
        }
        
        public static SKRect ToSkRect(this RectD rect)
        {
            return new SKRect((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
        }
        
        public static SKRect ToSkRect(this RectI rect)
        {
            return new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        }
    }
}
