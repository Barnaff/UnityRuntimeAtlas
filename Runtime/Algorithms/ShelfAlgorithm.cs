#if PACKING_BURST_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Shelf bin packing algorithm.
    /// Places rectangles in rows (shelves). When a row is full, a new shelf is started.
    /// Simple and fast, but can waste vertical space if items have varying heights.
    /// </summary>
    public sealed class ShelfAlgorithm : IPackingAlgorithm
    {
        private int _width;
        private int _height;
        private int _currentX;
        private int _currentY;
        private int _currentShelfHeight;
        private long _usedArea;
        private bool _isDisposed;

        // Keep track of free space in previous shelves to fill gaps
        private List<RectInt> _freeSpace;

        public int Width => _width;
        public int Height => _height;

        public ShelfAlgorithm()
        {
            _freeSpace = new List<RectInt>();
        }

        public void Initialize(int width, int height)
        {
            _width = width;
            _height = height;
            _currentX = 0;
            _currentY = 0;
            _currentShelfHeight = 0;
            _usedArea = 0;
            _freeSpace.Clear();
        }

        public bool TryPack(int width, int height, out RectInt result)
        {
            result = default;
            if (width <= 0 || height <= 0 || width > _width || height > _height)
            {
                return false;
            }

            // 1. Try to fit in free space from previous shelves (Waste Map)
            // Best Area Fit
            var bestFreeIndex = -1;
            var bestAreaFit = int.MaxValue;

            for (var i = 0; i < _freeSpace.Count; i++)
            {
                var free = _freeSpace[i];
                if (width <= free.width && height <= free.height)
                {
                    var areaFit = free.width * free.height - width * height;
                    if (areaFit < bestAreaFit)
                    {
                        bestAreaFit = areaFit;
                        bestFreeIndex = i;
                    }
                }
            }

            if (bestFreeIndex != -1)
            {
                var free = _freeSpace[bestFreeIndex];
                result = new RectInt(free.x, free.y, width, height);
                _usedArea += (long)width * height;

                // Remove used space and add remaining parts back to free space
                _freeSpace.RemoveAt(bestFreeIndex);
                
                // Split remaining space (Guillotine style split for the waste map)
                if (free.width > width)
                {
                    _freeSpace.Add(new RectInt(free.x + width, free.y, free.width - width, height));
                }
                
                if (free.height > height)
                {
                    _freeSpace.Add(new RectInt(free.x, free.y + height, free.width, free.height - height));
                }

                return true;
            }

            // 2. Try to fit in current shelf
            if (_currentX + width <= _width)
            {
                // Fits in current shelf width
                // Check if it fits in total height (if this shelf grows)
                if (_currentY + Math.Max(_currentShelfHeight, height) <= _height)
                {
                    result = new RectInt(_currentX, _currentY, width, height);
                    
                    // If this item is shorter than the shelf, add the space above it to free space
                    if (height < _currentShelfHeight)
                    {
                        _freeSpace.Add(new RectInt(_currentX, _currentY + height, width, _currentShelfHeight - height));
                    }

                    _currentX += width;
                    _currentShelfHeight = Math.Max(_currentShelfHeight, height);
                    _usedArea += (long)width * height;
                    return true;
                }
            }

            // 3. Start a new shelf
            // Add remaining space of current shelf to free space
            if (_width > _currentX)
            {
                _freeSpace.Add(new RectInt(_currentX, _currentY, _width - _currentX, _currentShelfHeight));
            }

            var nextY = _currentY + _currentShelfHeight;
            
            // Check if we can start a new shelf
            if (nextY + height <= _height)
            {
                _currentY = nextY;
                _currentX = 0;
                _currentShelfHeight = height;
                
                result = new RectInt(_currentX, _currentY, width, height);
                _currentX += width;
                _usedArea += (long)width * height;
                return true;
            }

            return false;
        }

        public void Free(RectInt rect)
        {
            _freeSpace.Add(rect);
            _usedArea -= (long)rect.width * rect.height;
        }

        public void Resize(int newWidth, int newHeight)
        {
            // Add new space to the right of current shelf as free space?
            // Or just let the algorithm naturally use it when starting new shelves?
            // For simplicity, we just update dimensions. 
            // The algorithm will naturally use the new width for new shelves.
            // The new height allows more shelves.
            
            // If we grew horizontally, the current shelf can extend
            // But we don't need to do anything special, _width check handles it.
            
            _width = newWidth;
            _height = newHeight;
        }

        public void Clear()
        {
            Initialize(_width, _height);
        }

        public float GetFillRatio()
        {
            if (_width == 0 || _height == 0)
            {
                return 0f;
            }
            return (float)_usedArea / (_width * _height);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }
            _isDisposed = true;
            _freeSpace.Clear();
            _freeSpace = null;
        }
    }
}
#endif
