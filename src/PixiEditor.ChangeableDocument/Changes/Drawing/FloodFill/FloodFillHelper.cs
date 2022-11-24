﻿using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using ChunkyImageLib.Operations;
using PixiEditor.ChangeableDocument.Changeables.Interfaces;
using PixiEditor.ChangeableDocument.Debugging;
using PixiEditor.DrawingApi.Core.ColorsImpl;
using PixiEditor.DrawingApi.Core.Numerics;
using PixiEditor.DrawingApi.Core.Surface;
using PixiEditor.DrawingApi.Core.Surface.ImageData;
using PixiEditor.DrawingApi.Core.Surface.Vector;

namespace PixiEditor.ChangeableDocument.Changes.Drawing.FloodFill;

public static class FloodFillHelper
{
    private const byte InSelection = 1;
    private const byte Visited = 2;
    
    private static readonly VecI Up = new VecI(0, -1);
    private static readonly VecI Down = new VecI(0, 1);
    private static readonly VecI Left = new VecI(-1, 0);
    private static readonly VecI Right = new VecI(1, 0);

    private static FloodFillChunkCache CreateCache(HashSet<Guid> membersToFloodFill, IReadOnlyDocument document)
    {
        if (membersToFloodFill.Count == 1)
        {
            Guid guid = membersToFloodFill.First();
            var member = document.FindMemberOrThrow(guid);
            if (member is IReadOnlyFolder folder)
                return new FloodFillChunkCache(membersToFloodFill, document.StructureRoot);
            return new FloodFillChunkCache(((IReadOnlyLayer)member).LayerImage);
        }
        return new FloodFillChunkCache(membersToFloodFill, document.StructureRoot);
    }

    public static Dictionary<VecI, Chunk> FloodFill(
        HashSet<Guid> membersToFloodFill,
        IReadOnlyDocument document,
        VectorPath? selection,
        VecI startingPos,
        Color drawingColor)
    {
        if (selection is not null && !selection.Contains(startingPos.X + 0.5f, startingPos.Y + 0.5f))
            return new();

        int chunkSize = ChunkResolution.Full.PixelSize();

        FloodFillChunkCache cache = CreateCache(membersToFloodFill, document);

        VecI initChunkPos = OperationHelper.GetChunkPos(startingPos, chunkSize);
        VecI imageSizeInChunks = (VecI)(document.Size / (double)chunkSize).Ceiling();
        VecI initPosOnChunk = startingPos - initChunkPos * chunkSize;
        Color colorToReplace = cache.GetChunk(initChunkPos).Match(
            (Chunk chunk) => chunk.Surface.GetSRGBPixel(initPosOnChunk),
            static (EmptyChunk _) => Colors.Transparent
        );

        if ((colorToReplace.A == 0 && drawingColor.A == 0) || colorToReplace == drawingColor)
            return new();

        RectI globalSelectionBounds = (RectI?)selection?.TightBounds ?? new RectI(VecI.Zero, document.Size);

        // Pre-multiplies the color and convert it to floats. Since floats are imprecise, a range is used.
        // Used for faster pixel checking
        ColorBounds colorRange = new(colorToReplace);
        ulong uLongColor = drawingColor.ToULong();

        Dictionary<VecI, Chunk> drawingChunks = new();
        HashSet<VecI> processedEmptyChunks = new();
        // flood fill chunks using a basic 4-way approach with a stack (each chunk is kinda like a pixel)
        // once the chunk is filled all places where it spills over to neighboring chunks are saved in the stack
        Stack<(VecI chunkPos, VecI posOnChunk)> positionsToFloodFill = new();
        positionsToFloodFill.Push((initChunkPos, initPosOnChunk));
        while (positionsToFloodFill.Count > 0)
        {
            var (chunkPos, posOnChunk) = positionsToFloodFill.Pop();

            if (!drawingChunks.ContainsKey(chunkPos))
            {
                var chunk = Chunk.Create();
                chunk.Surface.DrawingSurface.Canvas.Clear(Colors.Transparent);
                drawingChunks[chunkPos] = chunk;
            }
            var drawingChunk = drawingChunks[chunkPos];
            var referenceChunk = cache.GetChunk(chunkPos);

            // don't call floodfill if the chunk is empty
            if (referenceChunk.IsT1)
            {
                if (colorToReplace.A == 0 && !processedEmptyChunks.Contains(chunkPos))
                {
                    drawingChunk.Surface.DrawingSurface.Canvas.Clear(drawingColor);
                    for (int i = 0; i < chunkSize; i++)
                    {
                        if (chunkPos.Y > 0)
                            positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y - 1), new(i, chunkSize - 1)));
                        if (chunkPos.Y < imageSizeInChunks.Y - 1)
                            positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y + 1), new(i, 0)));
                        if (chunkPos.X > 0)
                            positionsToFloodFill.Push((new(chunkPos.X - 1, chunkPos.Y), new(chunkSize - 1, i)));
                        if (chunkPos.X < imageSizeInChunks.X - 1)
                            positionsToFloodFill.Push((new(chunkPos.X + 1, chunkPos.Y), new(0, i)));
                    }
                    processedEmptyChunks.Add(chunkPos);
                }
                continue;
            }

            // use regular flood fill for chunks that have something in them
            var reallyReferenceChunk = referenceChunk.AsT0;
            var maybeArray = FloodFillChunk(
                reallyReferenceChunk,
                drawingChunk,
                selection,
                globalSelectionBounds,
                chunkPos,
                chunkSize,
                uLongColor,
                drawingColor,
                posOnChunk,
                colorRange);

            if (maybeArray is null)
                continue;
            for (int i = 0; i < chunkSize; i++)
            {
                if (chunkPos.Y > 0 && maybeArray[i] == Visited)
                    positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y - 1), new(i, chunkSize - 1)));
                if (chunkPos.Y < imageSizeInChunks.Y - 1 && maybeArray[chunkSize * (chunkSize - 1) + i] == Visited)
                    positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y + 1), new(i, 0)));
                if (chunkPos.X > 0 && maybeArray[i * chunkSize] == Visited)
                    positionsToFloodFill.Push((new(chunkPos.X - 1, chunkPos.Y), new(chunkSize - 1, i)));
                if (chunkPos.X < imageSizeInChunks.X - 1 && maybeArray[i * chunkSize + (chunkSize - 1)] == Visited)
                    positionsToFloodFill.Push((new(chunkPos.X + 1, chunkPos.Y), new(0, i)));
            }
        }
        return drawingChunks;
    }
    
    private static unsafe byte[]? FloodFillChunk(
        Chunk referenceChunk,
        Chunk drawingChunk,
        VectorPath? selection,
        RectI globalSelectionBounds,
        VecI chunkPos,
        int chunkSize,
        ulong colorBits,
        Color color,
        VecI pos,
        ColorBounds bounds)
    {
        if (referenceChunk.Surface.GetSRGBPixel(pos) == color || drawingChunk.Surface.GetSRGBPixel(pos) == color)
            return null;

        byte[] pixelStates = new byte[chunkSize * chunkSize];
        DrawSelection(pixelStates, selection, globalSelectionBounds, chunkPos, chunkSize);

        using var refPixmap = referenceChunk.Surface.DrawingSurface.PeekPixels();
        Half* refArray = (Half*)refPixmap.GetPixels();

        using var drawPixmap = drawingChunk.Surface.DrawingSurface.PeekPixels();
        Half* drawArray = (Half*)drawPixmap.GetPixels();

        Stack<VecI> toVisit = new();
        toVisit.Push(pos);

        while (toVisit.Count > 0)
        {
            VecI curPos = toVisit.Pop();
            int pixelOffset = curPos.X + curPos.Y * chunkSize;
            Half* drawPixel = drawArray + pixelOffset * 4;
            Half* refPixel = refArray + pixelOffset * 4;
            *(ulong*)drawPixel = colorBits;
            pixelStates[pixelOffset] = Visited;

            if (curPos.X > 0 && pixelStates[pixelOffset - 1] == InSelection && bounds.IsWithinBounds(refPixel - 4))
                toVisit.Push(new(curPos.X - 1, curPos.Y));
            if (curPos.X < chunkSize - 1 && pixelStates[pixelOffset + 1] == InSelection && bounds.IsWithinBounds(refPixel + 4))
                toVisit.Push(new(curPos.X + 1, curPos.Y));
            if (curPos.Y > 0 && pixelStates[pixelOffset - chunkSize] == InSelection && bounds.IsWithinBounds(refPixel - 4 * chunkSize))
                toVisit.Push(new(curPos.X, curPos.Y - 1));
            if (curPos.Y < chunkSize - 1 && pixelStates[pixelOffset + chunkSize] == InSelection && bounds.IsWithinBounds(refPixel + 4 * chunkSize))
                toVisit.Push(new(curPos.X, curPos.Y + 1));
        }
        return pixelStates;
    }

    /// <summary>
    /// Use skia to set all pixels in array that are inside selection to InSelection
    /// </summary>
    private static unsafe void DrawSelection(byte[] array, VectorPath? selection, RectI globalBounds, VecI chunkPos, int chunkSize)
    {
        if (selection is null)
        {
            selection = new VectorPath();
            selection.AddRect(globalBounds);
        }

        RectI localBounds = globalBounds.Offset(-chunkPos * chunkSize).Intersect(new(0, 0, chunkSize, chunkSize));
        if (localBounds.IsZeroOrNegativeArea)
            return;
        VectorPath shiftedSelection = new VectorPath(selection);
        shiftedSelection.Transform(Matrix3X3.CreateTranslation(-chunkPos.X * chunkSize, -chunkPos.Y * chunkSize));

        fixed (byte* arr = array)
        {
            using DrawingSurface drawingSurface = DrawingSurface.Create(
                new ImageInfo(localBounds.Right, localBounds.Bottom, ColorType.Gray8, AlphaType.Opaque), (IntPtr)arr, chunkSize);
            drawingSurface.Canvas.ClipPath(shiftedSelection);
            drawingSurface.Canvas.Clear(new Color(InSelection, InSelection, InSelection));
            drawingSurface.Canvas.Flush();
        }
    }
    
    private static MagicWandVisualizer visualizer = new MagicWandVisualizer(Path.Combine("Debugging", "MagicWand"));

    public static VectorPath GetFloodFillSelection(VecI startingPos, HashSet<Guid> membersToFloodFill,
        IReadOnlyDocument document)
    {
        if(startingPos.X < 0 || startingPos.Y < 0 || startingPos.X >= document.Size.X || startingPos.Y >= document.Size.Y)
            return new VectorPath();
        
        int chunkSize = ChunkResolution.Full.PixelSize();

        FloodFillChunkCache cache = CreateCache(membersToFloodFill, document);

        VecI initChunkPos = OperationHelper.GetChunkPos(startingPos, chunkSize);
        VecI imageSizeInChunks = (VecI)(document.Size / (double)chunkSize).Ceiling();
        VecI initPosOnChunk = startingPos - initChunkPos * chunkSize;
        

        Color colorToReplace = cache.GetChunk(initChunkPos).Match(
            (Chunk chunk) => chunk.Surface.GetSRGBPixel(initPosOnChunk),
            static (EmptyChunk _) => Colors.Transparent
        );
        
        ColorBounds colorRange = new(colorToReplace);

        HashSet<VecI> processedEmptyChunks = new();
        HashSet<VecI> processedPositions = new();
        Stack<(VecI chunkPos, VecI posOnChunk)> positionsToFloodFill = new();
        positionsToFloodFill.Push((initChunkPos, initPosOnChunk));

        Lines lines = new();
        
        VectorPath selection = new();
        while (positionsToFloodFill.Count > 0)
        {
            var (chunkPos, posOnChunk) = positionsToFloodFill.Pop();
            var referenceChunk = cache.GetChunk(chunkPos);

            // don't call floodfill if the chunk is empty
            if (referenceChunk.IsT1)
            {
                if (colorToReplace.A == 0 && !processedEmptyChunks.Contains(chunkPos))
                {
                    ProcessEmptySelectionChunk(lines, chunkPos, document.Size, imageSizeInChunks, chunkSize);
                    for (int i = 0; i < chunkSize; i++)
                    {
                        if (chunkPos.Y > 0)
                            positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y - 1), new(i, chunkSize - 1)));
                        if (chunkPos.Y < imageSizeInChunks.Y - 1)
                            positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y + 1), new(i, 0)));
                        if (chunkPos.X > 0)
                            positionsToFloodFill.Push((new(chunkPos.X - 1, chunkPos.Y), new(chunkSize - 1, i)));
                        if (chunkPos.X < imageSizeInChunks.X - 1)
                            positionsToFloodFill.Push((new(chunkPos.X + 1, chunkPos.Y), new(0, i)));
                    }
                    processedEmptyChunks.Add(chunkPos);
                }
                continue;
            }

            // use regular flood fill for chunks that have something in them
            var reallyReferenceChunk = referenceChunk.AsT0;
            
            VecI globalPos = chunkPos * chunkSize + posOnChunk;
            
            if(processedPositions.Contains(globalPos))
                continue;

            var maybeArray = GetChunkFloodFill(
                reallyReferenceChunk,
                chunkSize,
                chunkPos * chunkSize,
                document.Size,
                posOnChunk,
                colorRange, lines);
            

            processedPositions.Add(globalPos);
            if (maybeArray is null)
                continue;
            for (int i = 0; i < chunkSize; i++)
            {
                if (chunkPos.Y > 0 && maybeArray[i] == Visited) // Top
                    positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y - 1), new(i, chunkSize - 1)));
                if (chunkPos.Y < imageSizeInChunks.Y - 1 && maybeArray[chunkSize * (chunkSize - 1) + i] == Visited) // Bottom
                    positionsToFloodFill.Push((new(chunkPos.X, chunkPos.Y + 1), new(i, 0)));
                if (chunkPos.X > 0 && maybeArray[i * chunkSize] == Visited) // Left
                    positionsToFloodFill.Push((new(chunkPos.X - 1, chunkPos.Y), new(chunkSize - 1, i)));
                if (chunkPos.X < imageSizeInChunks.X - 1 && maybeArray[i * chunkSize + (chunkSize - 1)] == Visited) // Right
                    positionsToFloodFill.Push((new(chunkPos.X + 1, chunkPos.Y), new(0, i)));
            }
        }

        if (lines.LineDict.Any(x => x.Value.Count > 0))
        {
            selection = BuildContour(lines);
        }
        
        visualizer.GenerateVisualization(document.Size.X, document.Size.Y, 500, 500);

        return selection;
    }

    private static void ProcessEmptySelectionChunk(Lines lines, VecI chunkPos, VecI realSize,
        VecI imageSizeInChunks, int chunkSize)
    {
        bool isEdgeChunk = chunkPos.X == 0 || chunkPos.Y == 0 || chunkPos.X == imageSizeInChunks.X - 1 ||
                           chunkPos.Y == imageSizeInChunks.Y - 1;
        if (isEdgeChunk)
        {
            bool isTopEdge = chunkPos.Y == 0;
            bool isBottomEdge = chunkPos.Y == imageSizeInChunks.Y - 1;
            bool isLeftEdge = chunkPos.X == 0;
            bool isRightEdge = chunkPos.X == imageSizeInChunks.X - 1;

            int posX = chunkPos.X * chunkSize;
            int posY = chunkPos.Y * chunkSize;

            int endX = posX + chunkSize;
            int endY = posY + chunkSize;

            endX = Math.Clamp(endX, 0, realSize.X);
            endY = Math.Clamp(endY, 0, realSize.Y);
            

            if (isTopEdge)
            {
                AddLine(new(new(posX, posY), new(endX, posY)), lines, Right);
            }

            if (isBottomEdge)
            {
                AddLine(new(new(endX, endY), new(posX, endY)), lines, Left);
            }

            if (isLeftEdge)
            {
                AddLine(new(new(posX, endY), new(posX, posY)), lines, Up);
            }
            
            if (isRightEdge)
            {
                AddLine(new(new(endX, posY), new(endX, endY)), lines, Down);
            }
        }
    }

    public static VectorPath BuildContour(Lines lines)
    {
        VectorPath selection = new();

        List<Line> remainingLines = lines.ToList(); // I'm not sure how to avoid this yet

        Line startingLine = remainingLines[0];
        VecI prevPos = startingLine.End;
        VecI prevDir = startingLine.NormalizedDirection;
        selection.MoveTo(startingLine.Start);
        selection.LineTo(startingLine.End);
        lines.RemoveLine(startingLine);
        for (var i = 1; i < remainingLines.Count; i++)
        {
            var line = remainingLines[i];
            Line nextLine;
            if (!lines.TryGetLine(prevPos, prevDir, out nextLine))
            {
                nextLine = line;
                selection.MoveTo(nextLine.Start);
                prevPos = nextLine.End;
                prevDir = nextLine.NormalizedDirection;
                lines.RemoveLine(nextLine);
                remainingLines.RemoveAt(i);
                i--;
                continue;
            }

            if (prevDir != nextLine.NormalizedDirection)
            {
                selection.LineTo(prevPos);
            }

            prevDir = nextLine.NormalizedDirection;
            prevPos = nextLine.End;
            lines.RemoveLine(nextLine);
            remainingLines.Remove(nextLine);
            i--;
        }
        
        selection.MoveTo(startingLine.End);
        selection.Close();
        return selection;
    }

    private static unsafe byte[]? GetChunkFloodFill(
        Chunk referenceChunk,
        int chunkSize,
        VecI chunkOffset,
        VecI documentSize,
        VecI pos,
        ColorBounds bounds, Lines lines)
    {
        if (!bounds.IsWithinBounds(referenceChunk.Surface.GetSRGBPixel(pos))) return null;
        byte[] pixelStates = new byte[chunkSize * chunkSize];

        using var refPixmap = referenceChunk.Surface.DrawingSurface.PeekPixels();
        Half* refArray = (Half*)refPixmap.GetPixels();
        
        Stack<VecI> toVisit = new();
        toVisit.Push(pos);

        while (toVisit.Count > 0)
        {
            VecI curPos = toVisit.Pop();

            int pixelOffset = curPos.X + curPos.Y * chunkSize;
            VecI globalPos = curPos + chunkOffset;
            Half* refPixel = refArray + pixelOffset * 4;
            
            if(!bounds.IsWithinBounds(refPixel)) continue;
            
            pixelStates[pixelOffset] = Visited;
            
            AddCornerLines(documentSize, chunkOffset, lines, curPos, chunkSize);
            AddFillContourLines(chunkSize, chunkOffset, bounds, lines, curPos, pixelStates, pixelOffset, refPixel, toVisit,  globalPos, documentSize);
        }
        
        return pixelStates;
    }

    private static unsafe void AddFillContourLines(int chunkSize, VecI chunkOffset, ColorBounds bounds, Lines lines,
        VecI curPos, byte[] pixelStates, int pixelOffset, Half* refPixel, Stack<VecI> toVisit, VecI globalPos, VecI documentSize)
    {
        // Left pixel
        if (curPos.X > 0 && pixelStates[pixelOffset - 1] != Visited)
        {
            if (bounds.IsWithinBounds(refPixel - 4) && globalPos.X - 1 >= 0)
            {
                toVisit.Push(new(curPos.X - 1, curPos.Y));
            }
            else
            {
                AddLine(
                    new Line(
                        new VecI(curPos.X, curPos.Y + 1) + chunkOffset,
                        new VecI(curPos.X, curPos.Y) + chunkOffset), lines, Up);
            }
        }

        // Right pixel
        if (curPos.X < chunkSize - 1 && pixelStates[pixelOffset + 1] != Visited)
        {
            if (bounds.IsWithinBounds(refPixel + 4) && globalPos.X + 1 < documentSize.X)
            {
                toVisit.Push(new(curPos.X + 1, curPos.Y));
            }
            else
            {
                AddLine(
                    new Line(
                        new VecI(curPos.X + 1, curPos.Y) + chunkOffset,
                        new VecI(curPos.X + 1, curPos.Y + 1) + chunkOffset), lines, Down);
            }
        }

        // Top pixel
        if (curPos.Y > 0 && pixelStates[pixelOffset - chunkSize] != Visited)
        {
            if (bounds.IsWithinBounds(refPixel - 4 * chunkSize) && globalPos.Y - 1 >= 0)
            {
                toVisit.Push(new(curPos.X, curPos.Y - 1));
            }
            else
            {
                AddLine(
                    new Line(
                        new VecI(curPos.X + 1, curPos.Y) + chunkOffset,
                        new VecI(curPos.X, curPos.Y) + chunkOffset), lines, Right);
            }
        }

        //Bottom pixel
        if (curPos.Y < chunkSize - 1 && pixelStates[pixelOffset + chunkSize] != Visited)
        {
            if (bounds.IsWithinBounds(refPixel + 4 * chunkSize) && globalPos.Y + 1 < documentSize.Y)
            {
                toVisit.Push(new(curPos.X, curPos.Y + 1));
            }
            else
            {
                AddLine(
                    new Line(
                        new VecI(curPos.X + 1, curPos.Y + 1) + chunkOffset,
                        new VecI(curPos.X, curPos.Y + 1) + chunkOffset), lines, Left);
            }
        }
    }

    private static void AddCornerLines(VecI documentSize, VecI chunkOffset, Lines lines, VecI curPos, int chunkSize)
    {
        VecI clampedPos = new(
            Math.Clamp(curPos.X, 0, documentSize.X - 1),
            Math.Clamp(curPos.Y, 0, documentSize.Y - 1));

        if (curPos.X == 0)
        {
            AddLine(
                new Line(
                    new VecI(clampedPos.X, clampedPos.Y + 1) + chunkOffset,
                    new VecI(clampedPos.X, clampedPos.Y) + chunkOffset), lines, Up);
        }

        if (curPos.X == chunkSize - 1)
        {
            AddLine(
                new Line(
                    new VecI(clampedPos.X + 1, clampedPos.Y) + chunkOffset,
                    new VecI(clampedPos.X + 1, clampedPos.Y + 1) + chunkOffset), lines, Down);
        }

        if (curPos.Y == 0)
        {
            AddLine(
                new Line(
                    new VecI(clampedPos.X, clampedPos.Y) + chunkOffset,
                    new VecI(clampedPos.X + 1, clampedPos.Y) + chunkOffset), lines, Right);
        }

        if (curPos.Y == chunkSize - 1)
        {
            AddLine(
                new Line(
                    new VecI(clampedPos.X + 1, clampedPos.Y + 1) + chunkOffset,
                    new VecI(clampedPos.X, clampedPos.Y + 1) + chunkOffset), lines, Left);
        }
    }

    private static void AddLine(Line line, Lines lines, VecI direction)
    {
        VecI calculatedDir = (VecI)(line.End - line.Start).Normalized();
        
        // if line in opposite direction exists, remove it
        
        if (lines.TryCancelLine(line, direction))
        {
            return;
        }
        
        if(lines.LineDict.Any(x => x.Value.ContainsValue(line))) return;

        if (calculatedDir == direction)
        {
            lines.LineDict[direction][line.Start] = line;
            visualizer.Steps.Add(line);
        }
        else if(calculatedDir == -direction)
        {
            Line fixedLine = new Line(line.End, line.Start);
            lines.LineDict[direction][line.End] = fixedLine;
            visualizer.Steps.Add(fixedLine);
        }
        else
        {
            throw new Exception(
                $"Line direction {calculatedDir} is not perpendicular to the direction of the requested line direction {direction}");
        }
    }

    public struct Line
    {
        public bool Equals(Line other)
        {
            return Start.Equals(other.Start) && End.Equals(other.End) && NormalizedDirection.Equals(other.NormalizedDirection);
        }

        public override bool Equals(object? obj)
        {
            return obj is Line other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Start, End, NormalizedDirection);
        }

        public VecI Start { get; set; }
        public VecI End { get; set; }
        public VecI NormalizedDirection { get; }

        public Line(VecI start, VecI end)
        {
            Start = start;
            End = end;
            NormalizedDirection = (VecI)(end - start).Normalized();
        }
        
        public Line Extended(VecI point)
        {
            VecI start = Start;
            VecI end = End;
            if (point.X < Start.X) start.X = point.X;
            if (point.Y < Start.Y) start.Y = point.Y;
            if (point.X > End.X) end.X = point.X;
            if (point.Y > End.Y) end.Y = point.Y;
            
            return new Line(start, end);
        }
        
        public static bool operator ==(Line a, Line b)
        {
            return a.Start == b.Start && a.End == b.End;
        }
        
        public static bool operator !=(Line a, Line b)
        {
            return !(a == b);
        }
    }

    public class Lines : IEnumerable<Line>
    {
        public Dictionary<VecI, Dictionary<VecI, Line>> LineDict { get; set; } = new Dictionary<VecI, Dictionary<VecI, Line>>();

        public Lines()
        {
            LineDict[Right] = new Dictionary<VecI, Line>();
            LineDict[Down] = new Dictionary<VecI, Line>();
            LineDict[Left] = new Dictionary<VecI, Line>();
            LineDict[Up] = new Dictionary<VecI, Line>();
        }
        
        public bool TryGetLine(VecI start, VecI preferredDir, out Line line)
        {
            if(LineDict[preferredDir].TryGetValue(start, out line)) return true;

            VecI cachedOppositeDir = -preferredDir;
            
            foreach (var lineDict in LineDict.Values)
            {
                // Preferred was already checked, opposite is invalid
                if(lineDict == LineDict[preferredDir] || lineDict == LineDict[cachedOppositeDir]) continue;
                
                if (lineDict.TryGetValue(start, out line))
                {
                    return true;
                }
            }
            
            line = default;
            return false;
        }

        public IEnumerator<Line> GetEnumerator()
        {
            foreach (var upLines in LineDict[Up])
            {
                yield return upLines.Value;
            }
            
            foreach (var rightLines in LineDict[Right])
            {
                yield return rightLines.Value;
            }
            
            foreach (var downLines in LineDict[Down])
            {
                yield return downLines.Value;
            }
            
            foreach (var leftLines in LineDict[Left])
            {
                yield return leftLines.Value;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public void RemoveLine(Line line)
        {
            foreach (var lineDict in LineDict.Values)
            {
                VecI dictDir = lineDict == LineDict[Up] ? Up : lineDict == LineDict[Right] ? Right : lineDict == LineDict[Down] ? Down : Left;
                if(line.NormalizedDirection != dictDir) continue;
                lineDict.Remove(line.Start);
            }
        }

        public bool TryCancelLine(Line line, VecI direction)
        {
            bool cancelingLineExists = false;
            
            LineDict[-direction].TryGetValue(line.End, out Line cancelingLine);
            if (cancelingLine != default && cancelingLine.End == line.Start)
            {
                cancelingLineExists = true;
                LineDict[-direction].Remove(line.End);
            }
            
            LineDict[direction].TryGetValue(line.Start, out cancelingLine);
            if (cancelingLine != default && cancelingLine.End == line.End)
            {
                cancelingLineExists = true;
                LineDict[-direction].Remove(line.Start);
            }

            return cancelingLineExists;
        }
    }
}
