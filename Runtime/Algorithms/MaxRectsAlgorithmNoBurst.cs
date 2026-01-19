using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// MaxRects bin packing algorithm without Burst/NativeCollections dependencies.
    /// Provides the same packing logic as MaxRectsAlgorithm but uses standard C# collections.
    /// Used when burst packing is disabled to avoid potential native memory issues.
    /// </summary>
    public sealed class MaxRectsAlgorithmNoBurst : IPackingAlgorithm
    {
        private List<RectInt> _freeRects;
        private List<RectInt> _usedRects;
        private int _width;
        private int _height;
        private long _usedArea;
        
        public int Width => _width;
        public int Height => _height;
        
        public MaxRectsAlgorithmNoBurst()
        {
            _freeRects = new List<RectInt>();
            _usedRects = new List<RectInt>();
        }
        
        public void Initialize(int width, int height)
        {
            _width = width;
            _height = height;
            _usedArea = 0;
            
            _freeRects.Clear();
            _usedRects.Clear();
            _freeRects.Add(new RectInt(0, 0, width, height));
        }
        
        public bool TryPack(int width, int height, out RectInt result)
        {
            result = default;
            
            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[MaxRectsNoBurst.TryPack] Invalid dimensions: {width}x{height}");
                return false;
            }
            
            if (width > _width || height > _height)
            {
                Debug.Log($"[MaxRectsNoBurst.TryPack] Size {width}x{height} exceeds atlas {_width}x{_height}");
                return false;
            }
            
            // Find best position using Best Short Side Fit heuristic
            var bestScore1 = int.MaxValue;
            var bestScore2 = int.MaxValue;
            var bestIndex = -1;
            var bestRect = default(RectInt);
            
            for (var i = 0; i < _freeRects.Count; i++)
            {
                var freeRect = _freeRects[i];
                
                // Try to place without rotation
                if (freeRect.width >= width && freeRect.height >= height)
                {
                    // Create candidate rect
                    var candidateRect = new RectInt(freeRect.x, freeRect.y, width, height);
                    
                    // Verify this position doesn't overlap with any used rectangles
                    if (OverlapsAnyUsedRect(candidateRect))
                    {
                        continue;
                    }
                    
                    var leftoverHoriz = math.abs(freeRect.width - width);
                    var leftoverVert = math.abs(freeRect.height - height);
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
                Debug.LogWarning($"[MaxRectsNoBurst.TryPack] FAILED to pack {width}x{height}. Atlas: {_width}x{_height}, FreeRects: {_freeRects.Count}, UsedRects: {_usedRects.Count}");
                return false;
            }
            
            // Place the rectangle
            PlaceRect(bestRect);
            
            result = bestRect;
            _usedArea += (long)width * height;
            
            Debug.Log($"[MaxRectsNoBurst.TryPack] SUCCESS packed {width}x{height} at ({result.x}, {result.y}). UsedRects: {_usedRects.Count}, FreeRects: {_freeRects.Count}");
            return true;
        }
        
        private bool OverlapsAnyUsedRect(RectInt rect)
        {
            for (var i = 0; i < _usedRects.Count; i++)
            {
                if (RectsIntersect(rect, _usedRects[i]))
                {
                    return true;
                }
            }
            return false;
        }
        
        private static bool RectsIntersect(RectInt a, RectInt b)
        {
            return a.x < b.x + b.width && a.x + a.width > b.x &&
                   a.y < b.y + b.height && a.y + a.height > b.y;
        }
        
        private void PlaceRect(RectInt rect)
        {
            // Split overlapping free rectangles
            for (var i = _freeRects.Count - 1; i >= 0; i--)
            {
                if (SplitFreeRect(_freeRects[i], rect, out var newRects))
                {
                    _freeRects.RemoveAt(i);
                    _freeRects.AddRange(newRects);
                }
            }
            
            // Prune contained rectangles
            PruneFreeRects();
            
            // Add to used rects
            _usedRects.Add(rect);
        }
        
        private bool SplitFreeRect(RectInt freeRect, RectInt usedRect, out List<RectInt> newRects)
        {
            newRects = new List<RectInt>(4);
            
            // Check if they don't intersect
            if (usedRect.x >= freeRect.x + freeRect.width || usedRect.x + usedRect.width <= freeRect.x ||
                usedRect.y >= freeRect.y + freeRect.height || usedRect.y + usedRect.height <= freeRect.y)
            {
                return false;
            }
            
            // New rects logic similar to original MaxRects
            
            // Left
            if (usedRect.x > freeRect.x && usedRect.x < freeRect.x + freeRect.width)
            {
                newRects.Add(new RectInt(freeRect.x, freeRect.y, usedRect.x - freeRect.x, freeRect.height));
            }
            
            // Right
            if (usedRect.x + usedRect.width < freeRect.x + freeRect.width && usedRect.x + usedRect.width > freeRect.x)
            {
                newRects.Add(new RectInt(usedRect.x + usedRect.width, freeRect.y, freeRect.x + freeRect.width - (usedRect.x + usedRect.width), freeRect.height));
            }
            
            // Bottom
            if (usedRect.y > freeRect.y && usedRect.y < freeRect.y + freeRect.height)
            {
                newRects.Add(new RectInt(freeRect.x, freeRect.y, freeRect.width, usedRect.y - freeRect.y));
            }
            
            // Top
            if (usedRect.y + usedRect.height < freeRect.y + freeRect.height && usedRect.y + usedRect.height > freeRect.y)
            {
                newRects.Add(new RectInt(freeRect.x, usedRect.y + usedRect.height, freeRect.width, freeRect.y + freeRect.height - (usedRect.y + usedRect.height)));
            }
            
            return true;
        }

        private void PruneFreeRects()
        {
            for (int i = 0; i < _freeRects.Count; i++)
            {
                for (int j = i + 1; j < _freeRects.Count; j++)
                {
                    if (IsContainedIn(_freeRects[i], _freeRects[j]))
                    {
                        _freeRects.RemoveAt(i);
                        i--;
                        break;
                    }
                    
                    if (IsContainedIn(_freeRects[j], _freeRects[i]))
                    {
                        _freeRects.RemoveAt(j);
                        j--;
                    }
                }
            }
        }
        
        private static bool IsContainedIn(RectInt a, RectInt b)
        {
            return a.x >= b.x && a.y >= b.y &&
                   a.x + a.width <= b.x + b.width &&
                   a.y + a.height <= b.y + b.height;
        }

        public void Free(RectInt rect)
        {
            var r = new RectInt(rect.x, rect.y, rect.width, rect.height);
            
            // Remove from used
            for (var i = 0; i < _usedRects.Count; i++)
            {
                var used = _usedRects[i];
                if (used.x == r.x && used.y == r.y && used.width == r.width && used.height == r.height)
                {
                    _usedRects.RemoveAt(i);
                    _usedArea -= (long)rect.width * rect.height;
                    break;
                }
            }
            
            // Add back to free rects
            _freeRects.Add(r);
            
            // Merge with adjacent free rectangles - basic prune
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
                _freeRects.Add(new RectInt(_width, 0, newWidth - _width, newHeight));
            }

            // Add new free space on the bottom
            if (newHeight > _height)
            {
                _freeRects.Add(new RectInt(0, _height, _width, newHeight - _height));
            }

            // Corner piece extension if needed
            if (newWidth > _width && newHeight > _height)
            {
                for (var i = 0; i < _freeRects.Count; i++)
                {
                    var r = _freeRects[i];
                    if (r.x == _width && r.y == 0)
                    {
                        // We can't modify struct in list directly, replace it
                        _freeRects[i] = new RectInt(r.x, r.y, r.width, newHeight);
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
            _freeRects.Add(new RectInt(0, 0, _width, _height));
            _usedArea = 0;
        }

        public float GetFillRatio()
        {
            var totalArea = (long)_width * _height;
            return totalArea > 0 ? (float)_usedArea / totalArea : 0f;
        }
        
        public void Dispose()
        {
            _freeRects.Clear();
            _usedRects.Clear();
        }
    }
}

