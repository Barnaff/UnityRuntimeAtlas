using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// MaxRects bin packing algorithm.
    /// Provides high-quality packing with support for dynamic updates.
    /// Uses native memory for zero-GC operation.
    /// </summary>
    public sealed class MaxRectsAlgorithm : IPackingAlgorithm
    {
        private NativeList<int4> _freeRects; // x, y, width, height
        private NativeList<int4> _usedRects;
        private int _width;
        private int _height;
        private long _usedArea;
        private bool _isDisposed;

        public int Width => _width;
        public int Height => _height;

        public MaxRectsAlgorithm()
        {
            _freeRects = new NativeList<int4>(64, Allocator.Persistent);
            _usedRects = new NativeList<int4>(64, Allocator.Persistent);
        }

        public void Initialize(int width, int height)
        {
            _width = width;
            _height = height;
            _usedArea = 0;
            
            _freeRects.Clear();
            _usedRects.Clear();
            _freeRects.Add(new int4(0, 0, width, height));
        }

        public bool TryPack(int width, int height, out RectInt result)
        {
            result = default;
            
            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[MaxRects.TryPack] Invalid dimensions: {width}x{height}");
                return false;
            }
            
            if (width > _width || height > _height)
            {
                Debug.Log($"[MaxRects.TryPack] Size {width}x{height} exceeds atlas {_width}x{_height}");
                return false;
            }

            // Find best position using Best Short Side Fit heuristic
            var bestScore1 = int.MaxValue;
            var bestScore2 = int.MaxValue;
            var bestIndex = -1;
            var bestRect = default(int4);
            var candidatesChecked = 0;
            var candidatesSkippedOverlap = 0;

            for (var i = 0; i < _freeRects.Length; i++)
            {
                var freeRect = _freeRects[i];
                
                // Try to place without rotation
                if (freeRect.z >= width && freeRect.w >= height)
                {
                    candidatesChecked++;
                    
                    // Create candidate rect
                    var candidateRect = new int4(freeRect.x, freeRect.y, width, height);
                    
                    // CRITICAL: Verify this position doesn't overlap with any used rectangles
                    if (OverlapsAnyUsedRect(candidateRect))
                    {
                        candidatesSkippedOverlap++;
                        continue;
                    }
                    
                    var leftoverHoriz = math.abs(freeRect.z - width);
                    var leftoverVert = math.abs(freeRect.w - height);
                    var shortSideFit = math.min(leftoverHoriz, leftoverVert);
                    var longSideFit = math.max(leftoverHoriz, leftoverVert);

                    if (shortSideFit < bestScore1 || (shortSideFit == bestScore1 && longSideFit < bestScore2))
                    {
                        bestScore1 = shortSideFit;
                        bestScore2 = longSideFit;
                        bestIndex = i;
                        bestRect = candidateRect;
                    }
                }
            }

            if (bestIndex == -1)
            {
                Debug.LogWarning($"[MaxRects.TryPack] FAILED to pack {width}x{height}. Atlas: {_width}x{_height}, FreeRects: {_freeRects.Length}, UsedRects: {_usedRects.Length}, Candidates: {candidatesChecked}, SkippedOverlaps: {candidatesSkippedOverlap}");
                return false;
            }

            // Place the rectangle
            PlaceRect(bestRect);
            
            result = new RectInt(bestRect.x, bestRect.y, bestRect.z, bestRect.w);
            _usedArea += (long)width * height;
            
            Debug.Log($"[MaxRects.TryPack] SUCCESS packed {width}x{height} at ({result.x}, {result.y}). UsedRects: {_usedRects.Length}, FreeRects: {_freeRects.Length}");
            return true;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool OverlapsAnyUsedRect(int4 rect)
        {
            for (var i = 0; i < _usedRects.Length; i++)
            {
                if (RectsIntersect(rect, _usedRects[i]))
                {
                    return true;
                }
            }
            return false;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool RectsIntersect(int4 a, int4 b)
        {
            // Two rectangles intersect if they overlap in both X and Y
            return !(a.x + a.z <= b.x || b.x + b.z <= a.x ||
                     a.y + a.w <= b.y || b.y + b.w <= a.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void PlaceRect(int4 rect)
        {
            // Collect all new free rects from splitting
            var tempNewRects = new NativeList<int4>(16, Allocator.Temp);
            
            // Split overlapping free rectangles
            for (var i = _freeRects.Length - 1; i >= 0; i--)
            {
                if (SplitFreeRect(_freeRects[i], rect, out var newRects))
                {
                    // Add new split rects to temporary list
                    for (var j = 0; j < newRects.Length; j++)
                    {
                        var newRect = newRects[j];
                        // Only add valid rects (positive dimensions)
                        if (newRect.z > 0 && newRect.w > 0)
                        {
                            tempNewRects.Add(newRect);
                        }
                    }
                    newRects.Dispose();
                    
                    // Remove the original free rect
                    _freeRects.RemoveAtSwapBack(i);
                }
            }
            
            // Add all new rects to free list
            for (var i = 0; i < tempNewRects.Length; i++)
            {
                _freeRects.Add(tempNewRects[i]);
            }
            tempNewRects.Dispose();

            // Prune contained rectangles
            PruneFreeRects();
            
            // Add to used rects
            _usedRects.Add(rect);
        }

        private bool SplitFreeRect(int4 freeRect, int4 usedRect, out NativeArray<int4> newRects)
        {
            newRects = new NativeArray<int4>(4, Allocator.Temp);
            var count = 0;

            // Check if they don't intersect
            if (usedRect.x >= freeRect.x + freeRect.z || usedRect.x + usedRect.z <= freeRect.x ||
                usedRect.y >= freeRect.y + freeRect.w || usedRect.y + usedRect.w <= freeRect.y)
            {
                newRects.Dispose();
                return false;
            }

            // Left
            if (usedRect.x > freeRect.x)
            {
                newRects[count++] = new int4(freeRect.x, freeRect.y, usedRect.x - freeRect.x, freeRect.w);
            }

            // Right
            if (usedRect.x + usedRect.z < freeRect.x + freeRect.z)
            {
                var rightX = usedRect.x + usedRect.z;
                newRects[count++] = new int4(rightX, freeRect.y, freeRect.x + freeRect.z - rightX, freeRect.w);
            }

            // Bottom
            if (usedRect.y > freeRect.y)
            {
                newRects[count++] = new int4(freeRect.x, freeRect.y, freeRect.z, usedRect.y - freeRect.y);
            }

            // Top
            if (usedRect.y + usedRect.w < freeRect.y + freeRect.w)
            {
                int topY = usedRect.y + usedRect.w;
                newRects[count++] = new int4(freeRect.x, topY, freeRect.z, freeRect.y + freeRect.w - topY);
            }

            // Resize to actual count
            var result = new NativeArray<int4>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
                result[i] = newRects[i];
            
            newRects.Dispose();
            newRects = result;
            
            return true;
        }

        private void PruneFreeRects()
        {
            // Remove rectangles that are completely contained within another
            for (int i = 0; i < _freeRects.Length; i++)
            {
                for (int j = i + 1; j < _freeRects.Length; j++)
                {
                    if (IsContainedIn(_freeRects[i], _freeRects[j]))
                    {
                        _freeRects.RemoveAtSwapBack(i);
                        i--;
                        break;
                    }
                    
                    if (IsContainedIn(_freeRects[j], _freeRects[i]))
                    {
                        _freeRects.RemoveAtSwapBack(j);
                        j--;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsContainedIn(int4 a, int4 b)
        {
            return a.x >= b.x && a.y >= b.y &&
                   a.x + a.z <= b.x + b.z &&
                   a.y + a.w <= b.y + b.w;
        }

        public void Free(RectInt rect)
        {
            var r = new int4(rect.x, rect.y, rect.width, rect.height);
            
            // Remove from used
            for (var i = 0; i < _usedRects.Length; i++)
            {
                var used = _usedRects[i];
                if (used.x == r.x && used.y == r.y && used.z == r.z && used.w == r.w)
                {
                    _usedRects.RemoveAtSwapBack(i);
                    _usedArea -= (long)rect.width * rect.height;
                    break;
                }
            }

            // Add back to free rects
            _freeRects.Add(r);
            
            // Merge with adjacent free rectangles
            MergeFreeRects();
        }

        private void MergeFreeRects()
        {
            // Simple merge - just prune for now
            // Full merge is expensive and often not necessary at runtime
            PruneFreeRects();
        }

        public void Resize(int newWidth, int newHeight)
        {
            if (newWidth < _width || newHeight < _height)
            {
                throw new ArgumentException("Cannot shrink atlas with existing content");
            }

            if (newWidth == _width && newHeight == _height)
            {
                return;
            }

            // Add new free space on the right
            if (newWidth > _width)
            {
                _freeRects.Add(new int4(_width, 0, newWidth - _width, newHeight));
            }

            // Add new free space on the bottom
            if (newHeight > _height)
            {
                _freeRects.Add(new int4(0, _height, _width, newHeight - _height));
            }

            // Corner piece if both expanded
            if (newWidth > _width && newHeight > _height)
            {
                // The corner is already covered by the two rectangles above
                // But we need to extend the right strip
                for (var i = 0; i < _freeRects.Length; i++)
                {
                    var r = _freeRects[i];
                    if (r.x == _width && r.y == 0)
                    {
                        _freeRects[i] = new int4(r.x, r.y, r.z, newHeight);
                        break;
                    }
                }
            }

            _width = newWidth;
            _height = newHeight;
            
            PruneFreeRects();
        }

        public void Clear()
        {
            _freeRects.Clear();
            _usedRects.Clear();
            _freeRects.Add(new int4(0, 0, _width, _height));
            _usedArea = 0;
        }

        public float GetFillRatio()
        {
            var totalArea = (long)_width * _height;
            return totalArea > 0 ? (float)_usedArea / totalArea : 0f;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            
            if (_freeRects.IsCreated)
            {
                _freeRects.Dispose();
            }
            if (_usedRects.IsCreated)
            {
                _usedRects.Dispose();
            }
        }
    }
}