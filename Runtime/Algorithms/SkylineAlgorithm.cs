using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Skyline bin packing algorithm.
    /// Faster than MaxRects but may produce slightly less optimal packing.
    /// Uses native memory for zero-GC operation.
    /// </summary>
    public sealed class SkylineAlgorithm : IPackingAlgorithm
    {
        private struct SkylineNode
        {
            public int X;
            public int Y;
            public int Width;
        }

        private NativeList<SkylineNode> _skyline;
        private NativeList<int4> _usedRects;
        private int _width;
        private int _height;
        private long _usedArea;
        private bool _isDisposed;

        public int Width => _width;
        public int Height => _height;

        public SkylineAlgorithm()
        {
            _skyline = new NativeList<SkylineNode>(64, Allocator.Persistent);
            _usedRects = new NativeList<int4>(64, Allocator.Persistent);
        }

        public void Initialize(int width, int height)
        {
            _width = width;
            _height = height;
            _usedArea = 0;
            
            _skyline.Clear();
            _usedRects.Clear();
            
            // Initial skyline is a single node spanning the bottom
            _skyline.Add(new SkylineNode { X = 0, Y = 0, Width = width });
        }

        public bool TryPack(int width, int height, out RectInt result)
        {
            result = default;
            
            if (width <= 0 || height <= 0 || width > _width || height > _height)
                return false;

            int bestIndex = -1;
            int bestX = 0;
            int bestY = int.MaxValue;
            int bestWidth = 0;
            int bestScore = int.MaxValue;

            // Find the best position using Best-Fit strategy
            // We want to minimize the height increase and wasted space
            for (int i = 0; i < _skyline.Length; i++)
            {
                if (TryFit(i, width, height, out int y))
                {
                    // Calculate score: prefer lower Y, then minimize gap
                    // Score = Y + (gap * weight)
                    // This helps fill gaps before growing upwards
                    
                    int score = y;
                    
                    // Check if this placement fits perfectly in a gap
                    if (y + height < _height)
                    {
                        // If we fit perfectly in width, give bonus
                        if (i + 1 < _skyline.Length && _skyline[i].Width == width)
                        {
                            score -= 1000; // Bonus for perfect width fit
                        }
                    }

                    if (score < bestScore || (score == bestScore && _skyline[i].X < bestX))
                    {
                        bestIndex = i;
                        bestX = _skyline[i].X;
                        bestY = y;
                        bestWidth = width;
                        bestScore = score;
                    }
                }
            }

            if (bestIndex == -1)
                return false;

            // Place the rectangle
            AddSkylineLevel(bestIndex, bestX, bestY, bestWidth, height);
            
            result = new RectInt(bestX, bestY, width, height);
            _usedArea += (long)width * height;
            _usedRects.Add(new int4(bestX, bestY, width, height));
            
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryFit(int index, int width, int height, out int y)
        {
            y = 0;
            var node = _skyline[index];
            
            // Check if it fits horizontally
            if (node.X + width > _width)
                return false;

            int widthLeft = width;
            int i = index;
            
            // Find the highest point under the rectangle
            while (widthLeft > 0 && i < _skyline.Length)
            {
                var current = _skyline[i];
                y = math.max(y, current.Y);
                
                // Check if too high
                if (y + height > _height)
                    return false;

                widthLeft -= current.Width;
                i++;
            }

            // Check horizontal bounds
            if (widthLeft > 0)
                return false;

            return true;
        }

        private void AddSkylineLevel(int index, int x, int y, int width, int height)
        {
            var newNode = new SkylineNode
            {
                X = x,
                Y = y + height,
                Width = width
            };

            // Insert the new node
            _skyline.InsertRangeWithBeginEnd(index, index + 1);
            _skyline[index] = newNode;

            // Remove nodes that are covered by the new node
            for (int i = index + 1; i < _skyline.Length; i++)
            {
                var node = _skyline[i];
                var prev = _skyline[i - 1];

                if (node.X < prev.X + prev.Width)
                {
                    int shrink = prev.X + prev.Width - node.X;

                    if (shrink >= node.Width)
                    {
                        _skyline.RemoveAt(i);
                        i--;
                    }
                    else
                    {
                        _skyline[i] = new SkylineNode
                        {
                            X = node.X + shrink,
                            Y = node.Y,
                            Width = node.Width - shrink
                        };
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            MergeSkyline();
        }

        private void MergeSkyline()
        {
            for (int i = 0; i < _skyline.Length - 1; i++)
            {
                var current = _skyline[i];
                var next = _skyline[i + 1];
                
                if (current.Y == next.Y)
                {
                    _skyline[i] = new SkylineNode
                    {
                        X = current.X,
                        Y = current.Y,
                        Width = current.Width + next.Width
                    };
                    _skyline.RemoveAt(i + 1);
                    i--;
                }
            }
        }

        public void Free(RectInt rect)
        {
            // Skyline doesn't easily support removal
            // We just track it for area calculation
            var r = new int4(rect.x, rect.y, rect.width, rect.height);
            
            for (int i = 0; i < _usedRects.Length; i++)
            {
                var used = _usedRects[i];
                if (used.x == r.x && used.y == r.y && used.z == r.z && used.w == r.w)
                {
                    _usedRects.RemoveAtSwapBack(i);
                    _usedArea -= (long)rect.width * rect.height;
                    break;
                }
            }
            
            // Note: Space is not actually reclaimed with Skyline algorithm
            // A full repack would be needed to reclaim space
        }

        public void Resize(int newWidth, int newHeight)
        {
            if (newWidth < _width || newHeight < _height)
                throw new ArgumentException("Cannot shrink atlas with existing content");

            if (newWidth == _width && newHeight == _height)
                return;

            // Extend the last skyline node if width increased
            if (newWidth > _width && _skyline.Length > 0)
            {
                int lastIndex = _skyline.Length - 1;
                var last = _skyline[lastIndex];
                
                // Check if we need to add a new node at ground level
                if (last.X + last.Width == _width)
                {
                    // Add new node at ground level for the new space
                    _skyline.Add(new SkylineNode
                    {
                        X = _width,
                        Y = 0,
                        Width = newWidth - _width
                    });
                }
            }

            _width = newWidth;
            _height = newHeight;
            
            MergeSkyline();
        }

        public void Clear()
        {
            _skyline.Clear();
            _usedRects.Clear();
            _skyline.Add(new SkylineNode { X = 0, Y = 0, Width = _width });
            _usedArea = 0;
        }

        public float GetFillRatio()
        {
            long totalArea = (long)_width * _height;
            return totalArea > 0 ? (float)_usedArea / totalArea : 0f;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            
            if (_skyline.IsCreated) _skyline.Dispose();
            if (_usedRects.IsCreated) _usedRects.Dispose();
        }
    }
}
