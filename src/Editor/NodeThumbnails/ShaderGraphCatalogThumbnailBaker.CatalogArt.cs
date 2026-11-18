using System;
using System.Collections.Generic;
using UnityEngine;
using Kind = DCFApixels.WhimTex.ShaderGraphNodeKind;

namespace DCFApixels.WhimTex
{
    internal static partial class ShaderGraphCatalogThumbnailBaker
    {
        static readonly Color ArtInk = new Color32(92, 219, 235, 255);
        static readonly Color ArtGold = new Color32(255, 186, 103, 255);
        static readonly Color ArtGuide = new Color32(107, 133, 157, 255);
        static readonly Color ArtMuted = new Color32(63, 82, 103, 255);
        static readonly Color ArtWhite = new Color32(225, 235, 246, 255);
        static readonly Color[] ChannelInks = {
            new Color32(255, 104, 111, 255), new Color32(116, 226, 139, 255),
            new Color32(116, 167, 255, 255), new Color32(205, 153, 255, 255)
        };

        static readonly Dictionary<Kind, Action<ThumbnailPainter>> CatalogArtwork = new()
        {
            { Kind.Add, p => OperatorArt(p, Kind.Add) },
            { Kind.Subtract, p => OperatorArt(p, Kind.Subtract) },
            { Kind.Multiply, p => OperatorArt(p, Kind.Multiply) },
            { Kind.Divide, p => OperatorArt(p, Kind.Divide) },
            { Kind.Lerp, p => RangeArt(p, Kind.Lerp) },
            { Kind.InverseLerp, p => RangeArt(p, Kind.InverseLerp) },
            { Kind.Remap, p => RangeArt(p, Kind.Remap) },
            { Kind.Split, p => ChannelFan(p, true) },
            { Kind.Combine, p => ChannelFan(p, false) },
            { Kind.Swizzle, SwizzleArt },
            { Kind.Append, AppendArt },
            { Kind.MatrixConstruction, MatrixConstructionArt },
            { Kind.MatrixSplit, MatrixSplitArt },
            { Kind.MatrixTranspose, MatrixTransposeArt },
            { Kind.MatrixDeterminant, MatrixDeterminantArt },
            { Kind.Length, LengthArt },
            { Kind.ConvertAngle, ConvertAngleArt },
            { Kind.EulerToQuaternion, p => QuaternionArt(p, Kind.EulerToQuaternion) },
            { Kind.QuaternionToEuler, p => QuaternionArt(p, Kind.QuaternionToEuler) },
            { Kind.RotateByQuaternion, p => QuaternionArt(p, Kind.RotateByQuaternion) },
            { Kind.QuaternionMultiply, p => QuaternionArt(p, Kind.QuaternionMultiply) },
            { Kind.QuaternionInverse, p => QuaternionArt(p, Kind.QuaternionInverse) },
            { Kind.QuaternionSlerp, p => QuaternionArt(p, Kind.QuaternionSlerp) },
            { Kind.CanvasData, CanvasDataArt },
            { Kind.TextureSize, TextureSizeArt },
            { Kind.SampleTexture, SamplePictureArt },
            { Kind.SampleInput, SamplePictureArt },
            { Kind.InstanceIndex, InstanceIndexArt },
            { Kind.Time, TimeArt },
            { Kind.LightCount, LightCountArt },
            { Kind.LightDirection, LightDirectionArt },
            { Kind.LightColor, LightColorArt },
            { Kind.Buffer, BufferArt },
            { Kind.Expression, ExpressionArt },
            { Kind.CustomFunction, CustomFunctionArt },
            { Kind.ForBegin, p => LoopArt(p, true) },
            { Kind.ForEnd, p => LoopArt(p, false) }
        };

        static Vector2 Pt(float x, float y) => new Vector2(x, y);

        static void OperatorArt(ThumbnailPainter p, Kind kind)
        {
            if (kind == Kind.Multiply)
            {
                p.Stroke(ArtInk, 2.35f, Pt(9, 9), Pt(23, 23));
                p.Stroke(ArtInk, 2.35f, Pt(9, 23), Pt(23, 9));
                return;
            }
            p.Stroke(ArtInk, 2.35f, Pt(7, 16), Pt(25, 16));
            if (kind == Kind.Add)
                p.Stroke(ArtInk, 2.35f, Pt(16, 7), Pt(16, 25));
            else if (kind == Kind.Divide)
            {
                p.Disc(ArtInk, Pt(16, 10), 1.6f);
                p.Disc(ArtInk, Pt(16, 22), 1.6f);
            }
        }

        static void RangeArt(ThumbnailPainter p, Kind kind)
        {
            const float left = 2.5f, right = 29.5f;
            const float inputY = 2.5f, outputY = 21.5f, bandHeight = 8;
            var input = (Left: 11f, Right: 21f, Low: Color.black, High: Color.white);
            var output = (Left: left, Right: right, Low: new Color(.04f, .65f, .72f), High: new Color(.97f, .33f, .06f));
            if (kind == Kind.InverseLerp)
                (input, output) = (output, input);
            else if (kind == Kind.Remap)
            {
                input = (5, 17, ((Color)new Color32(43, 67, 155, 255)).linear, ((Color)new Color32(86, 237, 221, 255)).linear);
                output = (11, 27.5f, ((Color)new Color32(109, 54, 145, 255)).linear, ((Color)new Color32(255, 174, 100, 255)).linear);
            }

            var upperLeft = Pt(input.Left, inputY + bandHeight);
            var upperRight = Pt(input.Right, inputY + bandHeight);
            var lowerLeft = Pt(output.Left, outputY);
            var lowerRight = Pt(output.Right, outputY);
            p.Polygon(ArtMuted, upperLeft, upperRight, lowerRight, lowerLeft);

            p.HorizontalGradient(new Rect(left, inputY, right - left, bandHeight), input.Low, input.High,
                kind == Kind.Remap ? left : input.Left, kind == Kind.Remap ? right : input.Right);
            p.HorizontalGradient(new Rect(left, outputY, right - left, bandHeight), output.Low, output.High,
                kind == Kind.Remap ? left : output.Left, kind == Kind.Remap ? right : output.Right);
            p.Outline(ArtGuide, left, inputY, right - left, bandHeight);
            p.Outline(ArtGuide, left, outputY, right - left, bandHeight);

            var guide = Color.Lerp(ArtGuide, ArtWhite, .45f);
            p.Stroke(guide, 1.25f, upperLeft, lowerLeft);
            p.Stroke(guide, 1.25f, upperRight, lowerRight);
        }

        static void ChannelFan(ThumbnailPainter p, bool split)
        {
            float bundleX = split ? 5 : 27;
            float channelsX = split ? 27 : 5;
            float sign = split ? 1 : -1;
            float destinationX = split ? 23.5f : 26;
            for (int i = 0; i < 3; i++)
            {
                float bundleY = 13 + 3 * i;
                float channelY = 7 + 9 * i;
                var ink = ChannelInks[i];
                p.Stroke(ink, 1.45f, Pt(bundleX, bundleY), Pt(bundleX + 5 * sign, bundleY),
                    Pt(channelsX - 7 * sign, channelY), Pt(channelsX, channelY));
                float destinationY = split ? channelY : bundleY;
                p.Stroke(ink, .95f, Pt(destinationX - 2, destinationY - 1.35f),
                    Pt(destinationX, destinationY), Pt(destinationX - 2, destinationY + 1.35f));
                p.Box(ink, channelsX - 1.5f, channelY - 1.55f, 3.1f, 3.1f);
            }
            Brackets(p, ArtGuide, bundleX - 3, 9.5f, 6, 13);
        }

        static void SwizzleArt(ThumbnailPainter p)
        {
            int[] outputRows = { 2, 0, 1 };
            for (int i = 0; i < outputRows.Length; i++)
            {
                float from = 7 + i * 9;
                float to = 7 + outputRows[i] * 9;
                var ink = ChannelInks[i];
                p.Stroke(ink, 1.55f, Pt(5, from), Pt(9, from), Pt(23, to), Pt(27, to));
                p.Box(ink, 3, from - 2, 4, 4);
                p.Box(ink, 25, to - 2, 4, 4);
            }
        }

        static void AppendArt(ThumbnailPainter p)
        {
            for (int i = 0; i < 4; i++)
            {
                float x = 5 + 6 * i + (i > 1 ? 4 : 0);
                p.Box(ChannelInks[i], x - 2, 7, 4, 4);
                p.Box(ChannelInks[i], 4 + 6 * i, 22, 4, 4);
            }
            p.Stroke(ArtGuide, 1.15f, Pt(8, 12), Pt(8, 15), Pt(26, 15), Pt(26, 12));
            p.Arrow(ArtWhite, 1.35f, Pt(17, 15), Pt(17, 20), 2.25f);
            Brackets(p, ArtGuide, 2, 20, 26, 8);
        }

        static void Brackets(ThumbnailPainter p, Color ink, float x, float y, float width, float height)
        {
            p.Stroke(ink, 1, Pt(x + 1.5f, y), Pt(x, y), Pt(x, y + height), Pt(x + 1.5f, y + height));
            p.Stroke(ink, 1, Pt(x + width - 1.5f, y), Pt(x + width, y),
                Pt(x + width, y + height), Pt(x + width - 1.5f, y + height));
        }

        static void Matrix(ThumbnailPainter p, float x, float y, Func<int, int, Color> ink)
        {
            Brackets(p, ArtGuide, x - 2, y - 2, 13, 13);
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    p.Box(ink(row, column), x + column * 3.4f, y + row * 3.4f, 2.2f, 2.2f);
        }

        static void MatrixConstructionArt(ThumbnailPainter p)
        {
            for (int row = 0; row < 3; row++)
                p.Stroke(ChannelInks[row], 2, Pt(3, 9 + 7 * row), Pt(9, 9 + 7 * row));
            p.Arrow(ArtWhite, 1.15f, Pt(11.5f, 16), Pt(16.5f, 16), 2);
            Matrix(p, 20, 11.5f, (row, _) => ChannelInks[row]);
        }

        static void MatrixSplitArt(ThumbnailPainter p)
        {
            Matrix(p, 4, 11.5f, (row, _) => row == 1 ? ArtGold : ArtMuted);
            p.Arrow(ArtWhite, 1.15f, Pt(16.5f, 16), Pt(21, 16), 2);
            for (int column = 0; column < 3; column++)
                p.Box(ArtGold, 23 + 3 * column, 14.85f, 2.1f, 2.3f);
        }

        static void MatrixTransposeArt(ThumbnailPainter p)
        {
            Matrix(p, 3.5f, 7, (row, _) => row == 1 ? ArtGold : ArtMuted);
            Matrix(p, 19.5f, 16, (_, column) => column == 1 ? ArtGold : ArtMuted);
            p.Arrow(ArtWhite, 1.2f, Pt(11, 23), Pt(16.5f, 23), 2);
            p.Stroke(ArtGuide, 1, Pt(17, 9), Pt(24, 15));
        }

        static void MatrixDeterminantArt(ThumbnailPainter p)
        {
            p.Box(ArtMuted, 3, 11, 9, 9);
            p.Outline(ArtGuide, 3, 11, 9, 9);
            p.Arrow(ArtWhite, 1.25f, Pt(14, 16), Pt(19, 16), 2);
            p.Polygon(Color.Lerp(ArtGold, ArtMuted, .35f), Pt(23, 8), Pt(30, 8), Pt(27, 25), Pt(20, 25));
            p.Stroke(ArtGold, 1.2f, Pt(23, 8), Pt(30, 8), Pt(27, 25), Pt(20, 25), Pt(23, 8));
        }

        static void LengthArt(ThumbnailPainter p)
        {
            p.Stroke(ArtMuted, 1, Pt(6, 9), Pt(6, 24), Pt(27, 24));
            p.Arrow(ArtInk, 1.85f, Pt(6, 24), Pt(23, 9), 3.5f);
            p.Stroke(ArtGold, 1.25f, Pt(9, 27.5f), Pt(27, 11.5f));
            p.Stroke(ArtGold, 1.25f, Pt(7.6f, 25.8f), Pt(10.4f, 29.1f));
            p.Stroke(ArtGold, 1.25f, Pt(25.6f, 9.8f), Pt(28.4f, 13.1f));
        }

        static void ConvertAngleArt(ThumbnailPainter p)
        {
            Vector2 origin = Pt(8, 17);
            p.Stroke(ArtInk, 1.55f, Pt(25, 17), origin, Pt(17.5f, 4.5f));
            p.Arc(ArtGold, 1.6f, origin, Pt(10, 10), -53, 0);
            p.Arc(ArtWhite, 1.1f, Pt(5.5f, 23.5f), Pt(2, 2), 0, 360);
            p.Arrow(ArtGuide, 1, Pt(9, 24), Pt(14, 24), 1.8f);
            p.Stroke(ArtWhite, 1.05f, Pt(17, 27), Pt(17, 22.5f), Pt(19.5f, 22.5f));
            p.Stroke(ArtWhite, 1.05f, Pt(24, 27), Pt(24, 23), Pt(21, 23), Pt(21, 27), Pt(24, 27));
            p.Stroke(ArtWhite, 1.05f, Pt(29.5f, 20.5f), Pt(29.5f, 27), Pt(26.5f, 27), Pt(26.5f, 23), Pt(29.5f, 23));
        }

        static void QuaternionArt(ThumbnailPainter p, Kind kind)
        {
            Vector2 Center = Pt(16, 17);
            Vector2 Tip(float angle, float radius = 11) => Center + Pt(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad)) * radius;
            if (kind is Kind.EulerToQuaternion or Kind.QuaternionToEuler)
            {
                bool inverse = kind == Kind.QuaternionToEuler;
                var axes = Pt(inverse ? 25 : 8, 18);
                p.Arrow(ChannelInks[0], 1.4f, axes, axes + Pt(4, 2), 2);
                p.Arrow(ChannelInks[1], 1.4f, axes, axes + Pt(0, -8), 2);
                p.Arrow(ChannelInks[2], 1.4f, axes, axes + Pt(-5, 5), 2);
                var sphere = Pt(inverse ? 8 : 25, 17);
                p.Arc(ArtInk, 1.4f, sphere, Pt(5, 7), 0, 360);
                p.Arc(ArtGuide, 1, sphere, Pt(2, 7), 0, 360);
                p.Arc(ArtGold, 1.2f, sphere, Pt(5, 2.5f), 0, 360);
                p.Arrow(ArtWhite, 1.5f, Pt(13, 16), Pt(18, 16), 2.2f);
                return;
            }
            p.Arc(ArtMuted, 1, Center, Pt(11, 11), 0, 360);
            if (kind == Kind.QuaternionSlerp)
            {
                for (int i = 0; i <= 4; i++)
                {
                    var color = i == 2 ? ArtWhite : i == 0 ? ArtInk : i == 4 ? ArtGold : ArtGuide;
                    p.Arrow(color, i == 2 ? 1.8f : 1.1f, Center, Tip(-105 + i * 22.5f), 2.5f);
                }
                p.Arc(ArtWhite, 1, Center, Pt(13, 13), -105, -15);
            }
            else if (kind == Kind.QuaternionMultiply)
            {
                p.Arrow(ArtGuide, 1, Center, Tip(0), 2.5f);
                p.Arrow(ArtInk, 1.4f, Center, Tip(-45), 2.5f);
                p.Arrow(ArtWhite, 1.8f, Center, Tip(-100), 3);
                p.Arc(ArtInk, 1.5f, Center, Pt(8, 8), -45, 0);
                p.Arc(ArtGold, 1.5f, Center, Pt(11, 11), -100, -45);
                p.Arrow(ArtGold, 1.5f, Tip(-85), Tip(-100), 2.4f);
            }
            else if (kind == Kind.QuaternionInverse)
            {
                p.Arrow(ArtInk, 1.6f, Center, Tip(-65), 3);
                p.Arrow(ArtGold, 1.6f, Center, Tip(30), 3);
                p.Arc(ArtInk, 1.5f, Center, Pt(12, 12), 30, 190);
                p.Arrow(ArtInk, 1.5f, Tip(175, 12), Tip(190, 12), 2.5f);
                p.Arc(ArtGold, 1.5f, Center, Pt(9, 9), -160, -20);
                p.Arrow(ArtGold, 1.5f, Tip(-145, 9), Tip(-160, 9), 2.5f);
            }
            else
            {
                p.Arrow(ArtGuide, 1.3f, Center, Tip(20), 3);
                p.Arrow(ArtInk, 2, Center, Tip(-75), 3.3f);
                p.Arc(ArtGold, 1.7f, Center, Pt(12, 12), -75, 20);
                p.Arrow(ArtGold, 1.7f, Tip(-60, 12), Tip(-75, 12), 3);
            }
            p.Disc(ArtWhite, Center, 1.3f);
        }

        static void CanvasDataArt(ThumbnailPainter p)
        {
            p.Box(ArtMuted, 4, 5, 19, 16);
            p.Outline(ArtInk, 4, 5, 19, 16);
            p.Stroke(ArtGuide, 1, Pt(10.3f, 5), Pt(10.3f, 21));
            p.Stroke(ArtGuide, 1, Pt(16.7f, 5), Pt(16.7f, 21));
            p.Stroke(ArtGuide, 1, Pt(4, 13), Pt(23, 13));
            Dimension(p, Pt(4, 26), Pt(23, 26));
            Dimension(p, Pt(27.5f, 5), Pt(27.5f, 21));
        }

        static void TextureSizeArt(ThumbnailPainter p)
        {
            MiniPicture(p, 7, 6, 18, 16);
            Brackets(p, ArtGold, 3.5f, 5, 25, 18);
            Dimension(p, Pt(7, 26.5f), Pt(25, 26.5f));
        }

        static void Dimension(ThumbnailPainter p, Vector2 a, Vector2 b)
        {
            p.Arrow(ArtGold, 1, a, b, 2.5f);
            p.Arrow(ArtGold, 1, b, a, 2.5f);
        }

        static void SamplePictureArt(ThumbnailPainter p)
        {
            const float display = ShaderGraphCatalogThumbnails.DisplaySize;
            const float height = display + 1;
            const float width = height * (18f / 16);
            MiniPicture(p, (display - width) * .5f, (display - height) * .5f, width, height, framed: false);
        }

        static void MiniPicture(ThumbnailPainter p, float x, float y, float width, float height, bool framed = true)
        {
            p.Box(new Color32(41, 79, 106, 255), x, y, width, height);
            p.Disc(ArtGold, Pt(x + width * .73f, y + height * .27f), height * .12f);
            p.Polygon(new Color32(74, 155, 150, 255), Pt(x, y + height), Pt(x, y + height * .84f),
                Pt(x + width * .31f, y + height * .37f), Pt(x + width * .76f, y + height));
            p.Polygon(new Color32(95, 203, 184, 255), Pt(x + width * .42f, y + height),
                Pt(x + width * .77f, y + height * .57f), Pt(x + width, y + height * .88f), Pt(x + width, y + height));
            if (framed) p.Outline(ArtGuide, x, y, width, height);
        }

        static void InstanceIndexArt(ThumbnailPainter p)
        {
            for (int i = 0; i < 3; i++)
                p.Box(ArtMuted, 3 + 9 * i, 9, 8, 14);
            p.Arc(ArtInk, 1.15f, Pt(7, 16), Pt(2, 3.1f), 0, 360);
            p.Stroke(ArtInk, 1.15f, Pt(14.3f, 14), Pt(16, 12.9f), Pt(16, 19.1f));
            p.Stroke(ArtInk, 1.15f, Pt(14.4f, 19.1f), Pt(17.6f, 19.1f));
            p.Stroke(ArtInk, 1.15f, Pt(23, 14), Pt(24, 12.9f), Pt(26.5f, 12.9f), Pt(27, 14.5f),
                Pt(23, 18.3f), Pt(23, 19.1f), Pt(27, 19.1f));
        }

        static void TimeArt(ThumbnailPainter p)
        {
            var center = Pt(16, 16);
            p.Arc(ArtInk, 1.6f, center, Pt(9, 9), 0, 360);
            p.Stroke(ArtWhite, 1.65f, Pt(16, 10), center, Pt(20, 18.5f));
            p.Arc(ArtGold, 1.35f, center, Pt(13, 13), -152, -55);
            p.Stroke(ArtGold, 1.35f, Pt(19, 4.6f), Pt(23.5f, 5.35f), Pt(22.2f, 1.9f));
        }

        static void LightSource(ThumbnailPainter p, Vector2 center, float radius, Color ink)
        {
            p.Disc(ink, center, radius);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                var direction = Pt(Mathf.Cos(angle), Mathf.Sin(angle));
                p.Stroke(ink, 1.05f, center + direction * (radius + 1.4f), center + direction * (radius + 3));
            }
        }

        static void LightCountArt(ThumbnailPainter p)
        {
            LightSource(p, Pt(6.5f, 10), 1.7f, ArtGold);
            LightSource(p, Pt(16, 10), 1.7f, ArtGold);
            LightSource(p, Pt(25.5f, 10), 1.7f, ArtGold);
            p.Stroke(ArtWhite, 1.55f, Pt(13, 17.5f), Pt(19, 17.5f), Pt(16, 21),
                Pt(19, 22), Pt(19, 25), Pt(17, 26.5f), Pt(13, 26));
        }

        static void LightDirectionArt(ThumbnailPainter p)
        {
            LightSource(p, Pt(8.5f, 8.5f), 2.7f, ArtGold);
            p.Arrow(ArtInk, 1.95f, Pt(13.5f, 13.5f), Pt(23, 23), 3.5f);
            p.Stroke(ArtGuide, 1.6f, Pt(8, 26), Pt(28, 26));
        }

        static void LightColorArt(ThumbnailPainter p)
        {
            var light = new Color32(231, 153, 255, 255);
            var spot = new Color32(182, 97, 243, 255);
            p.Polygon(Color.Lerp(spot, ArtMuted, .55f), Pt(16, 8), Pt(27, 24), Pt(5, 24));
            p.Stroke(ArtGuide, 1.1f, Pt(3, 25), Pt(29, 25));
            p.Polygon(spot, Pt(6, 24), Pt(10, 21), Pt(22, 21), Pt(26, 24), Pt(22, 26.5f), Pt(10, 26.5f));
            LightSource(p, Pt(16, 8), 2.5f, light);
        }

        static void BufferArt(ThumbnailPainter p)
        {
            p.Box(ArtMuted, 12, 8.5f, 15, 14);
            p.Arc(ArtInk, 1.35f, Pt(19.5f, 22.5f), Pt(7.5f, 3.4f), 0, 180);
            p.Stroke(ArtInk, 1.35f, Pt(12, 8.5f), Pt(12, 22.5f));
            p.Stroke(ArtInk, 1.35f, Pt(27, 8.5f), Pt(27, 22.5f));
            p.Arc(ArtInk, 1.35f, Pt(19.5f, 8.5f), Pt(7.5f, 3.4f), 0, 360);
            MiniPicture(p, 4, 16, 13, 11);
        }

        static void ExpressionArt(ThumbnailPainter p)
        {
            p.Stroke(ArtInk, 2.15f, Pt(7, 12), Pt(17.5f, 24));
            p.Stroke(ArtInk, 2.15f, Pt(17.5f, 12), Pt(7, 24));
            p.Stroke(ArtGold, 1.45f, Pt(20, 8), Pt(21.5f, 6.5f), Pt(24.5f, 6.5f),
                Pt(25.5f, 8.5f), Pt(20, 13), Pt(25.5f, 13));
        }

        static void CustomFunctionArt(ThumbnailPainter p)
        {
            p.Stroke(ArtInk, 1.9f, Pt(10, 10), Pt(4, 16), Pt(10, 22));
            p.Stroke(ArtInk, 1.9f, Pt(22, 10), Pt(28, 16), Pt(22, 22));
            p.Stroke(ArtGold, 1.9f, Pt(19, 7.5f), Pt(13, 24.5f));
        }

        static void LoopArt(ThumbnailPainter p, bool begin)
        {
            CurlyBrace(p, opening: true, active: begin);
            CurlyBrace(p, opening: false, active: !begin);
            p.Arc(ArtWhite, 1.65f, Pt(16, 16), Pt(5.1f, 5.1f), 210, 510);
            p.Polygon(ArtWhite, Pt(11, 17.6f), Pt(14.5f, 19.9f), Pt(11.3f, 21.8f));
        }

        static void CurlyBrace(ThumbnailPainter p, bool opening, bool active)
        {
            Vector2 Point(float x, float y) => Pt(opening ? x : 32 - x, y);
            Color ink = !active ? ArtGuide : opening ? ArtInk : ArtGold;
            p.Stroke(ink, active ? 2.2f : 1.5f,
                Point(10, 6), Point(8.5f, 6), Point(7.5f, 6.5f), Point(6.8f, 7.6f),
                Point(6.6f, 9.2f), Point(6.6f, 12.5f), Point(6.3f, 14.1f),
                Point(5.5f, 15.3f), Point(4, 16),
                Point(5.5f, 16.7f), Point(6.3f, 17.9f), Point(6.6f, 19.5f),
                Point(6.6f, 22.8f), Point(6.8f, 24.4f), Point(7.5f, 25.5f),
                Point(8.5f, 26), Point(10, 26));
        }

        // Draw in 32 px display coordinates into the 64 px tile and extrude the edge texels.
        sealed class ThumbnailPainter
        {
            const float PixelScale = (float)ShaderGraphCatalogThumbnails.DisplaySize / Size;
            const float Feather = .32f;
            readonly Color[] pixels = new Color[Cell * Cell];

            internal ThumbnailPainter() => Array.Fill(pixels, new Color(.13f, .14f, .16f, 1));

            internal void Stroke(Color color, float width, params Vector2[] points)
            {
                for (int i = 1; i < points.Length; i++)
                {
                    Vector2 a = points[i - 1], b = points[i];
                    float radius = width * .5f;
                    Vector2 margin = Vector2.one * radius;
                    Paint(color, Vector2.Min(a, b) - margin, Vector2.Max(a, b) + margin,
                        p => Mathf.Sqrt(CurveSegmentDistanceSquared(p, a, b)) - radius);
                }
            }

            internal void Arrow(Color color, float width, Vector2 a, Vector2 b, float head)
            {
                Vector2 back = (a - b).normalized * head;
                Vector2 side = Pt(-back.y, back.x) * .60f;
                Stroke(color, width, a, b);
                Stroke(color, width, b + back + side, b, b + back - side);
            }

            internal void Arc(Color color, float width, Vector2 center, Vector2 radius, float start, float end)
            {
                int count = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(end - start) / 10));
                var points = new Vector2[count + 1];
                for (int i = 0; i <= count; i++)
                {
                    float angle = Mathf.Lerp(start, end, (float)i / count) * Mathf.Deg2Rad;
                    points[i] = center + Pt(radius.x * Mathf.Cos(angle), radius.y * Mathf.Sin(angle));
                }
                Stroke(color, width, points);
            }

            internal void Disc(Color color, Vector2 center, float radius)
            {
                Vector2 extent = Vector2.one * radius;
                Paint(color, center - extent, center + extent, p => Vector2.Distance(p, center) - radius);
            }

            internal void Outline(Color color, float x, float y, float width, float height) =>
                Stroke(color, 1.1f, Pt(x, y), Pt(x + width, y), Pt(x + width, y + height),
                    Pt(x, y + height), Pt(x, y));

            internal void Box(Color color, float x, float y, float width, float height) =>
                Polygon(color, Pt(x, y), Pt(x + width, y), Pt(x + width, y + height), Pt(x, y + height));

            internal void HorizontalGradient(Rect rect, Color low, Color high, float start, float end)
            {
                Paint(p => Color.Lerp(low, high, Mathf.InverseLerp(start, end, p.x)).gamma, rect.min, rect.max,
                    p => Mathf.Max(Mathf.Abs(p.x - rect.center.x) - rect.width * .5f,
                        Mathf.Abs(p.y - rect.center.y) - rect.height * .5f));
            }

            internal void Polygon(Color color, params Vector2[] points)
            {
                Vector2 min = points[0], max = min;
                float orientation = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    min = Vector2.Min(min, points[i]);
                    max = Vector2.Max(max, points[i]);
                    orientation += Cross(points[i], points[(i + 1) % points.Length]);
                }
                float sign = Mathf.Sign(orientation);
                var normals = new Vector2[points.Length];
                for (int i = 0; i < points.Length; i++)
                {
                    Vector2 edge = (points[(i + 1) % points.Length] - points[i]).normalized;
                    normals[i] = Pt(edge.y, -edge.x) * sign;
                }
                Paint(color, min, max, p =>
                {
                    float distance = float.NegativeInfinity;
                    for (int i = 0; i < points.Length; i++)
                        distance = Mathf.Max(distance, Vector2.Dot(p - points[i], normals[i]));
                    return distance;
                });
            }

            static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

            void Paint(Color color, Vector2 min, Vector2 max, Func<Vector2, float> distance)
                => Paint(_ => color, min, max, distance);

            void Paint(Func<Vector2, Color> color, Vector2 min, Vector2 max, Func<Vector2, float> distance)
            {
                int left = Mathf.Max(0, Mathf.FloorToInt((min.x - Feather) / PixelScale));
                int right = Mathf.Min(Size, Mathf.CeilToInt((max.x + Feather) / PixelScale));
                int top = Mathf.Max(0, Mathf.FloorToInt((min.y - Feather) / PixelScale));
                int bottom = Mathf.Min(Size, Mathf.CeilToInt((max.y + Feather) / PixelScale));
                for (int y = top; y < bottom; y++)
                {
                    int row = (Size - 1 - y + Padding) * Cell;
                    for (int x = left; x < right; x++)
                    {
                        var point = Pt((x + .5f) * PixelScale, (y + .5f) * PixelScale);
                        float d = distance(point);
                        float coverage = Mathf.Clamp01(.5f - d / (2 * Feather));
                        int index = row + x + Padding;
                        pixels[index] = Color.Lerp(pixels[index], color(point), coverage);
                    }
                }
            }

            internal Color[] Finish()
            {
                for (int y = 0; y < Cell; y++)
                    for (int x = 0; x < Cell; x++)
                    {
                        int sourceX = Mathf.Clamp(x, Padding, Padding + Size - 1);
                        int sourceY = Mathf.Clamp(y, Padding, Padding + Size - 1);
                        pixels[y * Cell + x] = pixels[sourceY * Cell + sourceX];
                    }
                return pixels;
            }
        }
    }
}
