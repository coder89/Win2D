// Copyright (c) Microsoft Corporation. All rights reserved.
//
// Licensed under the MIT License. See LICENSE.txt in the project root for license information.

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

#if !WINDOWS_UWP
using Vector2 = Microsoft.Graphics.Canvas.Numerics.Vector2;
#endif

namespace ExampleGallery
{
    public sealed partial class CustomTextLayouts : UserControl
    {
        Rect layoutBox;
        const float desiredFontSize = 20.0f;
        List<Rect> layoutRectangles;
        List<GlyphRun> glyphRuns;
        bool needsLayout;
        Point pointerPressPosition;

        public CustomTextLayouts()
        {
            this.InitializeComponent();

            DataContext = this;

            ShowLayoutRectangles = false;
            ShowSelectionBox = false;
            CurrentForcedTextDirectionOption = ForcedTextDirectionOption.ScriptDependent;
            CurrentLayoutShapeOption = LayoutShapeOption.TextOutline;
            ShouldJustify = true;
        }

        public enum LayoutShapeOption
        {
            Box,
            Ellipse,
            Triangle,
            TextOutline
        }
        public List<LayoutShapeOption> LayoutShapeOptions { get { return Utils.GetEnumAsList<LayoutShapeOption>(); } }
        public LayoutShapeOption CurrentLayoutShapeOption { get; set; }
        public bool ShouldJustify { get; set; }

        public enum ForcedTextDirectionOption
        {
            AlwaysLeftToRight,
            AlwaysRightToLeft,
            ScriptDependent
        }
        public List<ForcedTextDirectionOption> ForcedTextDirectionOptions { get { return Utils.GetEnumAsList<ForcedTextDirectionOption>(); } }
        public ForcedTextDirectionOption CurrentForcedTextDirectionOption { get; set; }
        public bool ShowLayoutRectangles { get; set; }
        public bool ShowSelectionBox { get; set; }

        class RectangleComparer : IComparer<Rect>
        {
            public int Compare(Rect a, Rect b)
            {
                return a.Left < b.Left ? -1 : 1;
            }
        }

        //
        // This is a helper class for turning any source geometry into a set of rectangles
        // we can use for text layout. For the purposes of this demo, and because it looks 
        // cool, we've added a way to layout based off of arbitrary path data.
        //
        class PathReader : ICanvasPathReceiver
        {
            Rect lineRegion;

            List<Rect> unsortedOutput;
            
            float[] figureTopEdge;
            float[] figureBottomEdge;

            public PathReader(Rect lr)
            {
                lineRegion = lr;
                unsortedOutput = new List<Rect>();
            }

            public void BeginFigure(Vector2 point, CanvasFigureFill fill)
            {
                figureTopEdge = null;
                figureBottomEdge = null;

                if (AreClose(point.Y, (float)lineRegion.Top))
                {
                    figureTopEdge = new float[2] { point.X, point.X };
                }
                else if (AreClose(point.Y, (float)lineRegion.Bottom))
                {
                    figureBottomEdge = new float[2] { point.X, point.X };
                }
            }
            public void AddArc(Vector2 point, float x, float y, float z, CanvasSweepDirection sweepDirection, CanvasArcSize arcSize)
            {
                throw new NotSupportedException("This is never called, since we use CanvasGeometrySimplification.Lines on the geometry.");
            }
            public void AddCubicBezier(Vector2 controlPoint1, Vector2 controlPoint2, Vector2 endPoint)
            {
                throw new NotSupportedException("This is never called, since we use CanvasGeometrySimplification.Lines on the geometry.");
            }

            bool AreClose(float v1, float v2, float thresh = 0.01f)
            {
                return (System.Math.Abs(v1 - v2) < thresh);
            }

            public void AddLine(Vector2 endPoint)
            {
                if (AreClose(endPoint.Y, (float)lineRegion.Top))
                {
                    if (figureTopEdge == null)
                    {
                        figureTopEdge = new float[2] { endPoint.X, endPoint.X };
                    }
                    else
                    {
                        figureTopEdge[0] = System.Math.Min(figureTopEdge[0], endPoint.X);
                        figureTopEdge[1] = System.Math.Max(figureTopEdge[1], endPoint.X);
                    }
                }
                else if (AreClose(endPoint.Y, (float)lineRegion.Bottom))
                {
                    if (figureBottomEdge == null)
                    {
                        figureBottomEdge = new float[2] { endPoint.X, endPoint.X };
                    }
                    else
                    {
                        figureBottomEdge[0] = System.Math.Min(figureBottomEdge[0], endPoint.X);
                        figureBottomEdge[1] = System.Math.Max(figureBottomEdge[1], endPoint.X);
                    }
                }

            }
            public void AddQuadraticBezier(Vector2 controlPoint, Vector2 endPoint)
            {
                throw new NotSupportedException("This is never called, since we use CanvasGeometrySimplification.Lines on the geometry.");
            }

            public void SetFilledRegionDetermination(CanvasFilledRegionDetermination filledRegionDetermination)
            {
                // Ignored
            }

            public void SetSegmentOptions(CanvasFigureSegmentOptions figureSegmentOptions)
            {
                // Ignored
            }

            public void EndFigure(CanvasFigureLoop figureLoop)
            {
                if (figureTopEdge == null || figureBottomEdge == null)
                    return; // The figure's area doesn't comprise a line.

                float left = System.Math.Max(figureTopEdge[0], figureBottomEdge[0]);
                float right = System.Math.Min(figureTopEdge[1], figureBottomEdge[1]);

                float width = right - left;

                if (width > 0)
                {
                    Rect rect = new Rect(left, lineRegion.Top, width, lineRegion.Height);
                    unsortedOutput.Add(rect);
                }
            }

            public List<Rect> GetRectangles()
            {
                return unsortedOutput;
            }
        }

        List<Rect> SplitGeometryIntoRectangles(CanvasGeometry geometry, float rectangleHeight, CanvasControl sender)
        {
            List<Rect> result = new List<Rect>();

            var geometryBounds = geometry.ComputeBounds();
            double left = geometryBounds.X;
            double top = geometryBounds.Y;
            double y = top;
            while (y < geometryBounds.Bottom)
            {
                var lineRegion = new Rect(left, y, geometryBounds.Width, rectangleHeight);
                var lineRegionGeometry = CanvasGeometry.CreateRectangle(sender, lineRegion);
                var compareResult = geometry.CompareWith(lineRegionGeometry);

                if (compareResult == CanvasGeometryRelation.Contains)
                {
                    // The whole line fits.
                    result.Add(lineRegion);
                }
                else if (compareResult == CanvasGeometryRelation.Disjoint)
                {
                    // The line doesn't fit, so skip it.
                }
                else if (compareResult == CanvasGeometryRelation.Overlap)
                {
                    var intersection = geometry.CombineWith(lineRegionGeometry, Matrix3x2.Identity, CanvasGeometryCombine.Intersect);

                    PathReader pathReader = new PathReader(lineRegion);
                    intersection.Simplify(CanvasGeometrySimplification.Lines).SendPathTo(pathReader);

                    var rectangles = pathReader.GetRectangles();
                    rectangles.Sort(new RectangleComparer());
                    result.AddRange(rectangles);
                }

                y += rectangleHeight;

            }

            return result;
        }

        private CanvasGeometry GetLayoutGeometry(CanvasControl sender)
        {
            CanvasGeometry geometry = null;

            if (CurrentLayoutShapeOption == LayoutShapeOption.Box)
            {
                geometry = CanvasGeometry.CreateRectangle(sender, layoutBox);
            }
            else if (CurrentLayoutShapeOption == LayoutShapeOption.Ellipse)
            {
                float cx = (float)(layoutBox.Left + layoutBox.Right) / 2;
                float cy = (float)(layoutBox.Top + layoutBox.Bottom) / 2;
                float rx = cx - (float)layoutBox.Left;
                float ry = cy - (float)layoutBox.Top;
                geometry = CanvasGeometry.CreateEllipse(sender, cx, cy, rx, ry);
            }
            else if (CurrentLayoutShapeOption == LayoutShapeOption.Triangle)
            {
                CanvasPathBuilder pathBuilder = new CanvasPathBuilder(sender);
                pathBuilder.BeginFigure((float)layoutBox.Left, (float)layoutBox.Top);
                pathBuilder.AddLine((float)layoutBox.Left + (float)(layoutBox.Width), (float)layoutBox.Top);
                pathBuilder.AddLine((float)layoutBox.Left, (float)layoutBox.Top + (float)(layoutBox.Height));
                pathBuilder.EndFigure(CanvasFigureLoop.Closed);
                geometry = CanvasGeometry.CreatePath(pathBuilder);
            }
            else if (CurrentLayoutShapeOption == LayoutShapeOption.TextOutline)
            {
                CanvasTextFormat textFormat = new CanvasTextFormat();
                textFormat.FontWeight = Windows.UI.Text.FontWeights.ExtraBold;
                textFormat.WordWrapping = CanvasWordWrapping.NoWrap;
                textFormat.FontFamily = fontPicker.CurrentFontFamily;
                CanvasTextLayout textLayout = new CanvasTextLayout(sender, textLayoutBoxInput.Text, textFormat, 0, 0);

                geometry = CanvasGeometry.CreateText(textLayout);
                var bounds = geometry.ComputeBounds();
                float scaleX = (float)layoutBox.Width / (float)bounds.Width;
                float scaleY = (float)layoutBox.Height / (float)bounds.Height;
                geometry = geometry.Transform(
                    Matrix3x2.CreateTranslation(-(float)bounds.X, -(float)bounds.Y) *
                    Matrix3x2.CreateScale(scaleX, scaleY) *
                    Matrix3x2.CreateTranslation((float)layoutBox.X, (float)layoutBox.Y));
            }

            return geometry;
        }

        struct FormattingSpan
        {
            public CanvasGlyph[] Glyphs;
            public CanvasFontFace FontFace;
            public float FontSize;
            public bool IsRightToLeft;
            public CanvasAnalyzedScript Script;
            public int[] ClusterMap;
            public CanvasGlyphShaping[] GlyphShaping;
            public CanvasCharacterRange Range;
            public bool NeedsAdditionalJustificationCharacters;
        }

        bool IsRightToLeft(int scriptNumber)
        {
            if (CurrentForcedTextDirectionOption == ForcedTextDirectionOption.AlwaysLeftToRight)
            {
                return false;
            }
            else if (CurrentForcedTextDirectionOption == ForcedTextDirectionOption.AlwaysRightToLeft)
            {
                return true;
            }
            else
            {
                if (scriptNumber >= 100 && scriptNumber < 200)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        List<FormattingSpan> EvaluateFormattingSpans(
            CanvasTextAnalyzer textAnalyzer,
            IReadOnlyList<KeyValuePair<CanvasCharacterRange, CanvasScaledFont>> fontRuns,
            IReadOnlyList<KeyValuePair<CanvasCharacterRange, CanvasAnalyzedScript>> scriptRuns,
            out float maxLineSpacing)
        {
            maxLineSpacing = 0;

            List<FormattingSpan> formattingSpans = new List<FormattingSpan>();

            //
            // Divide up our text space into spans of uniform font face and uniform script.
            //
            foreach (var scriptRun in scriptRuns)
            {
                var scriptProperties = textAnalyzer.GetScriptProperties(scriptRun.Value);

                bool isRightToLeft = IsRightToLeft(scriptProperties.IsoScriptNumber);

                foreach (var fontRun in fontRuns)
                {
                    int fontMatchEnd = fontRun.Key.CharacterIndex + fontRun.Key.CharacterCount;
                    int scriptEnd = scriptRun.Key.CharacterIndex + scriptRun.Key.CharacterCount;

                    if (fontRun.Key.CharacterIndex > scriptEnd)
                        continue;

                    if (fontMatchEnd < scriptRun.Key.CharacterIndex)
                        continue;

                    int rangeStart = System.Math.Max(fontRun.Key.CharacterIndex, scriptRun.Key.CharacterIndex);
                    int rangeEnd = System.Math.Min(fontMatchEnd, scriptEnd);
                    int length = rangeEnd - rangeStart;

                    float fontSize = desiredFontSize * fontRun.Value.ScaleFactor;

                    FormattingSpan formattingSpan = new FormattingSpan();
                    formattingSpan.IsRightToLeft = isRightToLeft;

                    int[] clusterMap;
                    bool[] isShapedAlone;
                    CanvasGlyphShaping[] glyphShaping;

                    CanvasCharacterRange range = new CanvasCharacterRange { CharacterIndex = rangeStart, CharacterCount = length };

                    // Evaluate which glyphs comprise the text.
                    formattingSpan.Glyphs = textAnalyzer.GetGlyphs(
                        range,
                        fontRun.Value.FontFace,
                        fontSize,
                        false, // isSideways
                        formattingSpan.IsRightToLeft,
                        scriptRun.Value,
                        "",
                        null,
                        null,
                        out clusterMap,
                        out isShapedAlone,
                        out glyphShaping);

                    formattingSpan.FontFace = fontRun.Value.FontFace;
                    formattingSpan.FontSize = fontSize;
                    formattingSpan.Script = scriptRun.Value;
                    formattingSpan.ClusterMap = clusterMap;
                    formattingSpan.GlyphShaping = glyphShaping;
                    formattingSpan.Range = range;
                    formattingSpan.NeedsAdditionalJustificationCharacters = scriptProperties.JustificationCharacter != null;

                    formattingSpans.Add(formattingSpan);

                    //
                    // For text which contains non-uniform font faces, CanvasTextLayout takes the maximum of
                    // all of line spacings and applies it as the overall line spacing. We do the same thing, here.
                    //
                    maxLineSpacing = System.Math.Max(maxLineSpacing, GetLineSpacing(formattingSpan.FontFace, fontSize));
                }
            }

            return formattingSpans;
        }

        class GlyphRun
        {
            public FormattingSpan FormattingSpan;
            public List<CanvasGlyph> Glyphs;
            public System.Numerics.Vector2 Position;
            
            public int LineNumber;

            private int firstGlyphIndex; // Indices within the formatting span.
            private int lastGlyphIndex;
            
            public CanvasCharacterRange GetRange()
            {
                int formattingSpanStartIndex = FormattingSpan.Range.CharacterIndex;
                int start = GetCharacterIndex(firstGlyphIndex, FormattingSpan.ClusterMap);
                int end = GetCharacterIndex(lastGlyphIndex, FormattingSpan.ClusterMap);
                int length = end - start + 1;

                return new CanvasCharacterRange {
                    CharacterIndex = formattingSpanStartIndex + start,
                    CharacterCount = length };
            }

            public void AddGlyph(int glyphIndex)
            {
                if (Glyphs.Count == 0)
                {
                    firstGlyphIndex = glyphIndex;
                }
                lastGlyphIndex = glyphIndex;

                Glyphs.Add(FormattingSpan.Glyphs[glyphIndex]);
            }

            public int[] GetClusterMap(CanvasCharacterRange range)
            {
                //
                // Create a cluster map for this character range. Because the cluster map
                // should reflect only the text positions in the range, we need to re-normalize
                // it (so that it starts at 0).
                //
                int[] clusterMap = new int[range.CharacterCount];

                int formattingSpanStartIndex = FormattingSpan.Range.CharacterIndex;

                int firstClusterMapValue = FormattingSpan.ClusterMap[range.CharacterIndex - formattingSpanStartIndex];

                for (int i=0; i < range.CharacterCount; ++i)
                {                    
                    int indexWithinFormattingSpan = range.CharacterIndex - formattingSpanStartIndex + i; // Cluster maps are per formatting span.

                    clusterMap[i] = FormattingSpan.ClusterMap[indexWithinFormattingSpan] - firstClusterMapValue;
                }
                return clusterMap;
            }

            public CanvasGlyphShaping[] GetShaping()
            {
                //
                // The shaping array is in terms of glyph indices. The formatting span contains all the shaping info for this glyph run.
                //
                CanvasGlyphShaping[] shaping = new CanvasGlyphShaping[Glyphs.Count];
                                
                for (int i = 0; i < Glyphs.Count; ++i)
                {
                    shaping[i] = FormattingSpan.GlyphShaping[firstGlyphIndex + i];
                }
                return shaping;
            }
        }

        // This method returns the current glyph run.
        GlyphRun BeginGlyphRun(Rect rectangle, float advance, List<GlyphRun> glyphRuns, FormattingSpan formattingSpan, int lineNumber)
        {
            GlyphRun glyphRun = new GlyphRun();

            glyphRun.FormattingSpan = formattingSpan;

            float glyphRunXPosition = formattingSpan.IsRightToLeft ? (float)rectangle.Right - advance : (float)rectangle.Left + advance;
            glyphRun.Position = new System.Numerics.Vector2(glyphRunXPosition, (float)rectangle.Bottom);
            glyphRun.Glyphs = new List<CanvasGlyph>();
            glyphRun.LineNumber = lineNumber;
            glyphRuns.Add(glyphRun);

            return glyphRun;
        }

        //
        // Returns the current glyph run, or null if there's no more layout boxes.
        //
        GlyphRun BeginNewLayoutBox(ref int rectangleIndex, List<Rect> rectangles, ref float glyphRunAdvance, ref int wordsPerLine, FormattingSpan formattingSpan, List<GlyphRun> glyphRuns)
        {
            rectangleIndex++;
            if (rectangleIndex >= rectangles.Count)
                return null;

            glyphRunAdvance = 0;
            wordsPerLine = 0;

            return BeginGlyphRun(rectangles[rectangleIndex], glyphRunAdvance, glyphRuns, formattingSpan, rectangleIndex);
        }

        static int GetCharacterIndex(int glyphIndex, int[] clusterMap)
        {
            int correspondingTextPosition = 0;
            for (int k = 0; k < clusterMap.Length; ++k)
            {
                if (clusterMap[k] == glyphIndex)
                    correspondingTextPosition = k;
            }
            return correspondingTextPosition;
        }

        List<GlyphRun> CreateGlyphRuns(List<Rect> rectangles, List<FormattingSpan> formattingSpans, CanvasAnalyzedBreakpoint[] analyzedBreakpoints)
        {
            List<GlyphRun> glyphRuns = new List<GlyphRun>();

            if (rectangles.Count == 0) return glyphRuns;

            int rectangleIndex = 0;

            float glyphRunAdvance = 0;

            int wordsPerLine = 0;

            for (int formattingSpanIndex = 0; formattingSpanIndex < formattingSpans.Count; formattingSpanIndex++)
            {
                var formattingSpan = formattingSpans[formattingSpanIndex];

                bool formattingSpanEndsInAdvancingTheLayoutBox = false;

                var currentGlyphRun = BeginGlyphRun(rectangles[rectangleIndex], glyphRunAdvance, glyphRuns, formattingSpan, rectangleIndex);

                var glyphs = formattingSpan.Glyphs;

                for (int i = 0; i < glyphs.Length; ++i)
                {
                    formattingSpanEndsInAdvancingTheLayoutBox = false;

                    //
                    // Will the next word fit in the box?
                    //
                    float wordAdvance = 0.0f;
                    int wordBoundary;
                    for (wordBoundary = i; wordBoundary < glyphs.Length; wordBoundary++)
                    {
                        int correspondingTextPosition = GetCharacterIndex(wordBoundary, formattingSpan.ClusterMap);

                        var afterThisCharacter = analyzedBreakpoints[correspondingTextPosition].BreakAfter;
                        var beforeNextCharacter = (correspondingTextPosition < formattingSpan.ClusterMap.Length - 1) ? analyzedBreakpoints[correspondingTextPosition + 1].BreakBefore : CanvasLineBreakCondition.Neutral;

                        // 
                        // The text for this demo doesn't have any hard line breaks.
                        //
                        System.Diagnostics.Debug.Assert(afterThisCharacter != CanvasLineBreakCondition.MustBreak);

                        if (afterThisCharacter == CanvasLineBreakCondition.CanBreak && beforeNextCharacter != CanvasLineBreakCondition.CannotBreak)
                            break;
                        wordAdvance += glyphs[wordBoundary].Advance;
                    }

                    if (glyphRunAdvance + wordAdvance < rectangles[rectangleIndex].Width) // It fits
                    {
                        for (int j = i; j <= wordBoundary; j++)
                        {
                            if (j < glyphs.Length)
                            {
                                currentGlyphRun.AddGlyph(j);

                                glyphRunAdvance += glyphs[j].Advance;
                            }
                        }
                        i = wordBoundary;
                        wordsPerLine++;
                    }
                    else // Doesn't fit
                    {
                        if (wordsPerLine == 0) // Need an emegency break?
                        {
                            int breakBoundary = i;
                            while (breakBoundary < glyphs.Length && glyphRunAdvance + glyphs[breakBoundary].Advance < rectangles[rectangleIndex].Width)
                            {
                                currentGlyphRun.AddGlyph(breakBoundary);

                                glyphRunAdvance += glyphs[breakBoundary].Advance;

                                breakBoundary++;
                            }
                            i = breakBoundary - 1;
                        }
                        else
                        {
                            i--; // Retry the glyph against the next rectangle.
                        }

                        currentGlyphRun = BeginNewLayoutBox(ref rectangleIndex, rectangles, ref glyphRunAdvance, ref wordsPerLine, formattingSpan, glyphRuns);

                        if (currentGlyphRun == null)
                            return glyphRuns;

                        formattingSpanEndsInAdvancingTheLayoutBox = true;
                    }
                }

                if (!formattingSpanEndsInAdvancingTheLayoutBox)
                {
                    //
                    // Every formatting span with a text direction change, we advance the layout box if we haven't already.
                    // A fuller-featured custom renderer may want to do something more complicated to support changes in 
                    // text direction inside the same layout box.
                    //
                    bool textDirectionWillChange = formattingSpanIndex < formattingSpans.Count - 1 &&
                        formattingSpan.IsRightToLeft != formattingSpans[formattingSpanIndex + 1].IsRightToLeft;

                    if (textDirectionWillChange)
                    {
                        currentGlyphRun = BeginNewLayoutBox(ref rectangleIndex, rectangles, ref glyphRunAdvance, ref wordsPerLine, formattingSpan, glyphRuns);

                        if (currentGlyphRun == null)
                            return glyphRuns;
                    }
                }
            }

            return glyphRuns;
        }

        CanvasJustificationOpportunity[] GetJustificationOpportunities(CanvasTextAnalyzer textAnalyzer, int startingGlyphRunIndex, int endingGlyphRunIndex, int glyphCount, out CanvasGlyph[] allGlyphs)
        {
            CanvasJustificationOpportunity[] justificationOpportunities = new CanvasJustificationOpportunity[glyphCount];
            allGlyphs = new CanvasGlyph[glyphCount];

            int glyphIndex = 0;

            for (int i = startingGlyphRunIndex; i < endingGlyphRunIndex; ++i)
            {
                if (glyphRuns[i].Glyphs.Count == 0)
                    continue;

                CanvasCharacterRange range = glyphRuns[i].GetRange();

                var glyphRunClusterMap = glyphRuns[i].GetClusterMap(range);
                var glyphRunShaping = glyphRuns[i].GetShaping();

                var justificationOpportunitiesThisGlyphRun = textAnalyzer.GetJustificationOpportunities(
                    range,
                    glyphRuns[i].FormattingSpan.FontFace,
                    glyphRuns[i].FormattingSpan.FontSize,
                    glyphRuns[i].FormattingSpan.Script,
                    glyphRunClusterMap,
                    glyphRunShaping);

                for (int j=0; j < glyphRuns[i].Glyphs.Count; ++j )
                {
                    justificationOpportunities[glyphIndex + j] = justificationOpportunitiesThisGlyphRun[j];
                    allGlyphs[glyphIndex + j] = glyphRuns[i].Glyphs[j];
                }
                glyphIndex += glyphRuns[i].Glyphs.Count;
            }

            return justificationOpportunities;
        }

        void SplitJustifiedGlyphsIntoRuns(CanvasTextAnalyzer textAnalyzer, int startingGlyphRunIndex, int endingGlyphRunIndex, CanvasGlyph[] justifiedGlyphs, bool needsAdditionalJustificationCharacters)
        {
            int glyphIndex = 0;

            float xPosition = glyphRuns[startingGlyphRunIndex].Position.X;
            for (int i = startingGlyphRunIndex; i < endingGlyphRunIndex; ++i)
            {
                if (glyphRuns[i].Glyphs.Count == 0)
                    continue;

                // Adjust glyph run positioning based on justification
                glyphRuns[i].Position = new Vector2() { X = xPosition, Y = glyphRuns[i].Position.Y };

                // Update running total glyph run advance
                for (int j = 0; j < glyphRuns[i].Glyphs.Count; j++)
                {
                    xPosition += glyphRuns[i].Glyphs[j].Advance;
                }

                if (needsAdditionalJustificationCharacters)
                {
                    // Replace the glyph data, since justification can modify glyphs                
                    CanvasGlyph[] justifiedGlyphsForThisGlyphRun = new CanvasGlyph[glyphRuns[i].Glyphs.Count];
                    for (int j = 0; j < glyphRuns[i].Glyphs.Count; j++)
                    {
                        justifiedGlyphsForThisGlyphRun[j] = justifiedGlyphs[glyphIndex + j];
                    }

                    CanvasCharacterRange range = glyphRuns[i].GetRange();

                    var glyphRunClusterMap = glyphRuns[i].GetClusterMap(range);
                    var glyphRunShaping = glyphRuns[i].GetShaping();
                    
                    CanvasGlyph[] newSetOfGlyphs = textAnalyzer.AddGlyphsAfterJustification(
                        glyphRuns[i].FormattingSpan.FontFace,
                        glyphRuns[i].FormattingSpan.FontSize,
                        glyphRuns[i].FormattingSpan.Script,
                        glyphRunClusterMap,
                        glyphRuns[i].Glyphs.ToArray(),
                        justifiedGlyphsForThisGlyphRun,
                        glyphRunShaping);

                    glyphRuns[i].Glyphs = new List<CanvasGlyph>(newSetOfGlyphs);
                }
                else
                {
                    for (int j = 0; j < glyphRuns[i].Glyphs.Count; j++)
                    {
                        glyphRuns[i].Glyphs[j] = justifiedGlyphs[glyphIndex + j];
                    }
                }

                glyphIndex += glyphRuns[i].Glyphs.Count;
            }
        }

        void JustifyLine(CanvasTextAnalyzer textAnalyzer, List<GlyphRun> glyphRuns, int startingGlyphRunIndex, int endingGlyphRunIndex, float layoutWidth, int glyphCountForLine)
        {
            CanvasGlyph[] allGlyphs;
            var justificationOpportunities = GetJustificationOpportunities(textAnalyzer, startingGlyphRunIndex, endingGlyphRunIndex, glyphCountForLine, out allGlyphs);

            CanvasGlyph[] justifiedGlyphs = textAnalyzer.ApplyJustificationOpportunities(
                layoutWidth,
                justificationOpportunities,
                allGlyphs);

            bool needsJustificationCharacters = glyphRuns[startingGlyphRunIndex].FormattingSpan.NeedsAdditionalJustificationCharacters;

            SplitJustifiedGlyphsIntoRuns(textAnalyzer, startingGlyphRunIndex, endingGlyphRunIndex, justifiedGlyphs, needsJustificationCharacters);
        }

        void Justify(CanvasTextAnalyzer textAnalyzer, List<GlyphRun> glyphRuns, List<Rect> layoutRectangles)
        {
            if (glyphRuns.Count == 0)
                return;

            int startingGlyphRunIndex = 0;
            int glyphCountForLine = 0;

            for (int i = 0; i < glyphRuns.Count; i++ )
            {
                if (glyphRuns[i].Glyphs.Count == 0)
                    continue;

                bool lineNumberChanged = i > 0 && glyphRuns[i].LineNumber != glyphRuns[i - 1].LineNumber;

                if (lineNumberChanged)
                {
                    int endingGlyphRunIndex = i;
                    var layoutWidth = (float)layoutRectangles[glyphRuns[i - 1].LineNumber].Width;
                    JustifyLine(textAnalyzer, glyphRuns, startingGlyphRunIndex, endingGlyphRunIndex, layoutWidth, glyphCountForLine);

                    startingGlyphRunIndex = endingGlyphRunIndex;
                    glyphCountForLine = 0;
                }

                glyphCountForLine += glyphRuns[i].Glyphs.Count;
            }

            // Do the last line, too.
            var lastLineLayoutWidth = (float)layoutRectangles[glyphRuns[glyphRuns.Count - 1].LineNumber].Width;
            JustifyLine(textAnalyzer, glyphRuns, startingGlyphRunIndex, glyphRuns.Count, lastLineLayoutWidth, glyphCountForLine);
        }

        private void EnsureLayout(CanvasControl sender)
        {
            if (!needsLayout)
                return;

            var geometry = GetLayoutGeometry(sender);

            string text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Integer lacinia odio lectus, eget luctus felis tincidunt sit amet. Maecenas vel ex porttitor, ultrices nunc feugiat, porttitor quam. Cras non interdum urna. In sagittis tempor leo quis laoreet. Sed pretium tellus ut commodo viverra. Ut volutpat in risus at aliquam. Sed faucibus vitae dolor ut commodo. Mauris mollis rhoncus libero ut porttitor. Suspendisse at egestas nunc. Proin non neque nibh. Mauris eu ornare arcu. Etiam non sem eleifend, imperdiet erat at, hendrerit ante. Pellentesque habitant morbi tristique senectus et netus et malesuada fames ac turpis egestas. Integer porttitor mauris eu pulvinar commodo. Interdum et malesuada fames ac ante ipsum primis in faucibus. Mauris ultricies fermentum sem sed consequat. Vestibulum iaculis dui nulla, nec pharetra dolor gravida in. Pellentesque vel nisi urna. Donec gravida nunc sed pellentesque feugiat. Aliquam iaculis enim non enim ultrices aliquam. In at leo sed lorem interdum bibendum at non enim.";
            var textFormat = new CanvasTextFormat();
            textFormat.FontFamily = fontPicker.CurrentFontFamily;

            CanvasTextAnalyzer textAnalyzer = new CanvasTextAnalyzer(text, CanvasTextDirection.LeftToRightThenTopToBottom);

            //
            // Figure out what fonts to use.
            //
            var fontResult = textAnalyzer.GetFonts(textFormat);

            //
            // Perform a script analysis on the text.
            //
            var scriptAnalysis = textAnalyzer.GetScript();

            float maxLineSpacing = 0;
            List<FormattingSpan> formattingSpans = EvaluateFormattingSpans(textAnalyzer, fontResult, scriptAnalysis, out maxLineSpacing);
            
            //
            // Perform line break analysis.
            //
            var breakpoints = textAnalyzer.GetBreakpoints();

            //
            // Get the rectangles to layout text into.
            //
            layoutRectangles = SplitGeometryIntoRectangles(geometry, maxLineSpacing, sender);
            
            //
            // Insert glyph runs into the layout boxes.
            //
            glyphRuns = CreateGlyphRuns(layoutRectangles, formattingSpans, breakpoints);

            if (ShouldJustify)
            {
                Justify(textAnalyzer, glyphRuns, layoutRectangles);
            }

            needsLayout = false;

        }

        private static float GetLineSpacing(CanvasFontFace fontFace, float fontSize)
        {
            return (fontFace.LineGap + fontFace.Ascent + fontFace.Descent) * fontSize;
        }

        CanvasStrokeStyle dashedStroke = new CanvasStrokeStyle() { DashStyle = CanvasDashStyle.Dash };

        private void Canvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
        {
            EnsureLayout(sender);

            if (ShowLayoutRectangles)
            {
                foreach (var layoutRectangle in layoutRectangles)
                {
                    args.DrawingSession.DrawRectangle(layoutRectangle, Colors.Gray);
                }
            }

            if (ShowSelectionBox)
            {
                args.DrawingSession.DrawRectangle(layoutBox, Colors.LightGray, 2.0f, dashedStroke);
            }

            var brush = new CanvasSolidColorBrush(sender, Colors.LightSkyBlue);

            foreach (GlyphRun g in glyphRuns)
            {
                if (g.Glyphs.Count > 0)
                {
                    uint bidiLevel = g.FormattingSpan.IsRightToLeft ? 1u : 0u;

                    args.DrawingSession.DrawGlyphRun(
                        g.Position,
                        g.FormattingSpan.FontFace,
                        g.FormattingSpan.FontSize,
                        g.Glyphs.ToArray(),
                        false, // isSideways
                        bidiLevel,
                        brush);
                }
            }
        }

        private void Canvas_CreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
        {
            needsLayout = true;
        }

        private void Canvas_PointerPressed(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            pointerPressPosition = e.GetCurrentPoint(canvas).Position;
            canvas.Invalidate();
            e.Handled = true;
        }

        private void Canvas_PointerMoved(object sender, Windows.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (e.Pointer.IsInContact)
            {
                layoutBox = new Rect(pointerPressPosition, e.GetCurrentPoint(canvas).Position);
                needsLayout = true;

                canvas.Invalidate();
                e.Handled = true;
            }
        }

        private void LayoutShapeOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            needsLayout = true;
            canvas.Invalidate();

            if (CurrentLayoutShapeOption == LayoutShapeOption.TextOutline)
            {
                textOutlineInputLabel.Visibility = Visibility.Visible;
                textLayoutBoxInput.Visibility = Visibility.Visible;
            }
            else
            {
                textOutlineInputLabel.Visibility = Visibility.Collapsed;
                textLayoutBoxInput.Visibility = Visibility.Collapsed;
            }
        }

        private void textLayoutBoxInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            needsLayout = true;
            canvas.Invalidate();
        }

        private void ForcedTextDirectionOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            needsLayout = true;
            canvas.Invalidate();
        }

        private void InvalidateCanvas(object sender, RoutedEventArgs e)
        {
            canvas.Invalidate();
        }

        private void fontPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            needsLayout = true;
            canvas.Invalidate();
        }

        private void canvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            layoutBox.X = (float)canvas.Size.Width / 8;
            layoutBox.Width = (float)canvas.Size.Width * 6 / 8;

            layoutBox.Y = (float)canvas.Size.Height / 8;
            layoutBox.Height = (float)canvas.Size.Height * 6 / 8;

            needsLayout = true;
        }

        private void Justify_ValueChanged(object sender, RoutedEventArgs e)
        {
            needsLayout = true;
            canvas.Invalidate();
        }

        private void control_Unloaded(object sender, RoutedEventArgs e)
        {
            // Explicitly remove references to allow the Win2D controls to get garbage collected
            canvas.RemoveFromVisualTree();
            canvas = null;
        }
    }
}