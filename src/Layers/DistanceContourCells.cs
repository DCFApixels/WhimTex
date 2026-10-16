using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DCFApixels.WhimTex
{
    internal struct DistanceContourCell
    {
        public int x, y;
        public float4 values;
    }

    internal static class DistanceContourCells
    {
        // Bound the temporary stream independently of canvas area. Main-thread
        // welding still consumes cells in the original row/column order.
        internal const int CellsPerBatch = 65536;

        internal static NativeStream Extract(NativeArray<Color32> input, int width, int height,
            byte threshold, int channel, int firstRow, int rowCount)
        {
            if (width < 1 || height < 1 || (long)width * height != input.Length
                || channel < 0 || channel > 4 || firstRow < 0 || rowCount < 1
                || (long)firstRow + rowCount > height - 1)
                throw new ArgumentOutOfRangeException(nameof(rowCount), "Invalid contour row range.");
            var cells = new NativeStream(rowCount, Allocator.TempJob);
            try
            {
                using (DistanceFieldTiming.Measure("contour.scanJobs"))
                    new ExtractRowsJob { input = input, writer = cells.AsWriter(), width = width,
                        firstRow = firstRow, threshold = threshold, channel = channel }
                        .Schedule(rowCount, 1).Complete();
                return cells;
            }
            catch { cells.Dispose(); throw; }
        }

        // No fast-math reassociation: threshold ties and channel values are part
        // of the contour contract. Interpolation/decider/welding stay unchanged.
        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct ExtractRowsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Color32> input;
            public NativeStream.Writer writer;
            public int width, firstRow, channel;
            public byte threshold;
            static float Value(Color32 color, int channel)
            {
                // The managed float-returning luminance expression retains wider
                // intermediates before rounding its return value. Explicit double
                // products preserve that boundary in Burst as well; coefficients
                // remain the original float constants, not new double constants.
                if (channel == 4)
                    return (float)((double)0.2126f * color.r + (double)0.7152f * color.g + (double)0.0722f * color.b);
                return DistanceFieldSource.Value(color, channel);
            }
            public void Execute(int row)
            {
                int y = firstRow + row;
                writer.BeginForEachIndex(row);
                float a = Value(input[y * width], channel) - threshold;
                float d = Value(input[(y + 1) * width], channel) - threshold;
                for (int x = 0; x + 1 < width; x++)
                {
                    float b = Value(input[y * width + x + 1], channel) - threshold;
                    float c = Value(input[(y + 1) * width + x + 1], channel) - threshold;
                    bool side = a > 0;
                    if (side != (b > 0) || side != (c > 0) || side != (d > 0))
                        writer.Write(new DistanceContourCell { x = x, y = y, values = new float4(a, b, c, d) });
                    a = b; d = c;
                }
                writer.EndForEachIndex();
            }
        }
    }
}
