using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Guillotine bin packing algorithm.
    /// Maintains a list of free rectangles. When a rectangle is placed, the free rectangle is split into two smaller ones.
    /// </summary>
    public sealed class GuillotineAlgorithm : IPackingAlgorithm
    {
        private List<RectInt> _freeRects;
        private int _width;
        private int _height;
        private long _usedArea;
        private bool _isDisposed;

        public int Width => _width;
        public int Height => _height;

        public GuillotineAlgorithm()
        {
            _freeRects = new List<RectInt>();
        }

        public void Initialize(int width, int height)
        {
            _width = width;
            _height = height;
            _usedArea = 0;
            _freeRects.Clear();
            _freeRects.Add(new RectInt(0, 0, width, height));
        }

        public bool TryPack(int width, int height, out RectInt result)
        {
            result = default;
            if (width <= 0 || height <= 0 || width > _width || height > _height)
                return false;

            // Find best free rectangle using Best Area Fit (BAF) heuristic
            int bestRectIndex = -1;
            int bestAreaFit = int.MaxValue;
            int bestShortSideFit = int.MaxValue;

            for (int i = 0; i < _freeRects.Count; i++)
            {
                RectInt freeRect = _freeRects[i];
                
                // Check if it fits
                if (width <= freeRect.width && height <= freeRect.height)
                {
                    int areaFit = freeRect.width * freeRect.height - width * height;
                    int shortSideFit = Math.Min(freeRect.width - width, freeRect.height - height);

                    if (areaFit < bestAreaFit || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit))
                    {
                        bestRectIndex = i;
                        bestAreaFit = areaFit;
                        bestShortSideFit = shortSideFit;
                    }
                }
            }

            if (bestRectIndex == -1)
                return false;

            // Place the rectangle
            RectInt freeNode = _freeRects[bestRectIndex];
            result = new RectInt(freeNode.x, freeNode.y, width, height);
            _usedArea += (long)width * height;

            // Split the remaining area
            // Split along the shorter axis (MIN heuristic)
            int w = freeNode.width - width;
            int h = freeNode.height - height;

            // Remove the used node
            _freeRects.RemoveAt(bestRectIndex);

            // Perform the split
            if (w <= h)
            {
                // Split vertically
                if (w > 0) _freeRects.Add(new RectInt(freeNode.x + width, freeNode.y, w, height));
                if (h > 0) _freeRects.Add(new RectInt(freeNode.x, freeNode.y + height, freeNode.width, h));
            }
            else
            {
                // Split horizontally
                if (w > 0) _freeRects.Add(new RectInt(freeNode.x + width, freeNode.y, w, freeNode.height));
                if (h > 0) _freeRects.Add(new RectInt(freeNode.x, freeNode.y + height, width, h));
            }

            // Merge free rectangles if possible (optional optimization)
            // MergeFreeRects();

            return true;
        }

        public void Free(RectInt rect)
        {
            _freeRects.Add(rect);
            _usedArea -= (long)rect.width * rect.height;
            // Ideally we should merge adjacent free rectangles here
        }

        public void Resize(int newWidth, int newHeight)
        {
            if (newWidth > _width)
            {
                // Add new free space to the right
                _freeRects.Add(new RectInt(_width, 0, newWidth - _width, _height));
            }
            
            if (newHeight > _height)
            {
                // Add new free space to the top
                _freeRects.Add(new RectInt(0, _height, newWidth, newHeight - _height));
            }

            _width = newWidth;
            _height = newHeight;
        }

        public void Clear()
        {
            _usedArea = 0;
            _freeRects.Clear();
            _freeRects.Add(new RectInt(0, 0, _width, _height));
        }

        public float GetFillRatio()
        {
            if (_width == 0 || _height == 0) return 0f;
            return (float)_usedArea / (_width * _height);
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            _freeRects.Clear();
            _freeRects = null;
        }
    }
}

