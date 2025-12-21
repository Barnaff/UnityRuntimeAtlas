using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Burst-compiled job for batch rectangle packing.
    /// Use for packing many rectangles at once with maximum performance.
    /// </summary>
    [BurstCompile]
    public struct BatchPackJob : IJob
    {
        [ReadOnly] public NativeArray<int2> Sizes; // width, height
        public NativeArray<int4> Results; // x, y, width, height (-1 for failed)
        public NativeList<int4> FreeRects;
        
        public int AtlasWidth;
        public int AtlasHeight;

        public void Execute()
        {
            for (int i = 0; i < Sizes.Length; i++)
            {
                var size = Sizes[i];
                if (TryPack(size.x, size.y, out var result))
                {
                    Results[i] = result;
                }
                else
                {
                    Results[i] = new int4(-1, -1, size.x, size.y);
                }
            }
        }

        private bool TryPack(int width, int height, out int4 result)
        {
            result = default;

            if (width <= 0 || height <= 0)
                return false;

            int bestScore1 = int.MaxValue;
            int bestScore2 = int.MaxValue;
            int bestIndex = -1;
            int4 bestRect = default;

            for (int i = 0; i < FreeRects.Length; i++)
            {
                var freeRect = FreeRects[i];

                if (freeRect.z >= width && freeRect.w >= height)
                {
                    int leftoverHoriz = math.abs(freeRect.z - width);
                    int leftoverVert = math.abs(freeRect.w - height);
                    int shortSideFit = math.min(leftoverHoriz, leftoverVert);
                    int longSideFit = math.max(leftoverHoriz, leftoverVert);

                    if (shortSideFit < bestScore1 || (shortSideFit == bestScore1 && longSideFit < bestScore2))
                    {
                        bestScore1 = shortSideFit;
                        bestScore2 = longSideFit;
                        bestIndex = i;
                        bestRect = new int4(freeRect.x, freeRect.y, width, height);
                    }
                }
            }

            if (bestIndex == -1)
                return false;

            PlaceRect(bestRect);
            result = bestRect;
            return true;
        }

        private void PlaceRect(int4 rect)
        {
            var newRects = new NativeList<int4>(4, Allocator.Temp);

            for (int i = FreeRects.Length - 1; i >= 0; i--)
            {
                var freeRect = FreeRects[i];

                // Check intersection
                if (rect.x >= freeRect.x + freeRect.z || rect.x + rect.z <= freeRect.x ||
                    rect.y >= freeRect.y + freeRect.w || rect.y + rect.w <= freeRect.y)
                    continue;

                // Split
                newRects.Clear();

                // Left
                if (rect.x > freeRect.x)
                    newRects.Add(new int4(freeRect.x, freeRect.y, rect.x - freeRect.x, freeRect.w));

                // Right
                if (rect.x + rect.z < freeRect.x + freeRect.z)
                {
                    int rightX = rect.x + rect.z;
                    newRects.Add(new int4(rightX, freeRect.y, freeRect.x + freeRect.z - rightX, freeRect.w));
                }

                // Bottom
                if (rect.y > freeRect.y)
                    newRects.Add(new int4(freeRect.x, freeRect.y, freeRect.z, rect.y - freeRect.y));

                // Top
                if (rect.y + rect.w < freeRect.y + freeRect.w)
                {
                    int topY = rect.y + rect.w;
                    newRects.Add(new int4(freeRect.x, topY, freeRect.z, freeRect.y + freeRect.w - topY));
                }

                FreeRects.RemoveAtSwapBack(i);

                for (int j = 0; j < newRects.Length; j++)
                    FreeRects.Add(newRects[j]);
            }

            newRects.Dispose();
            PruneFreeRects();
        }

        private void PruneFreeRects()
        {
            for (int i = 0; i < FreeRects.Length; i++)
            {
                for (int j = i + 1; j < FreeRects.Length; j++)
                {
                    if (IsContained(FreeRects[i], FreeRects[j]))
                    {
                        FreeRects.RemoveAtSwapBack(i);
                        i--;
                        break;
                    }

                    if (IsContained(FreeRects[j], FreeRects[i]))
                    {
                        FreeRects.RemoveAtSwapBack(j);
                        j--;
                    }
                }
            }
        }

        private static bool IsContained(int4 a, int4 b)
        {
            return a.x >= b.x && a.y >= b.y &&
                   a.x + a.z <= b.x + b.z &&
                   a.y + a.w <= b.y + b.w;
        }
    }

    /// <summary>
    /// Job for sorting rectangles by area (descending).
    /// </summary>
    [BurstCompile]
    public struct SortByAreaJob : IJob
    {
        public NativeArray<int2> Sizes;
        public NativeArray<int> Indices;

        public void Execute()
        {
            // Simple bubble sort for now - could use parallel sort for large arrays
            for (int i = 0; i < Indices.Length; i++)
                Indices[i] = i;

            for (int i = 0; i < Indices.Length - 1; i++)
            {
                for (int j = 0; j < Indices.Length - i - 1; j++)
                {
                    int areaA = Sizes[Indices[j]].x * Sizes[Indices[j]].y;
                    int areaB = Sizes[Indices[j + 1]].x * Sizes[Indices[j + 1]].y;

                    if (areaA < areaB)
                    {
                        var temp = Indices[j];
                        Indices[j] = Indices[j + 1];
                        Indices[j + 1] = temp;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parallel job for calculating total area of rectangles.
    /// </summary>
    [BurstCompile]
    public struct CalculateTotalAreaJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int2> Sizes;
        public NativeArray<long> PartialSums;

        public void Execute(int index)
        {
            PartialSums[index] = (long)Sizes[index].x * Sizes[index].y;
        }
    }
}
