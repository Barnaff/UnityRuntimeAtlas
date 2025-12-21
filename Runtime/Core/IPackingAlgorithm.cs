using System;
using UnityEngine;

namespace RuntimeAtlasPacker
{
    /// <summary>
    /// Interface for rectangle packing algorithms.
    /// </summary>
    public interface IPackingAlgorithm : IDisposable
    {
        /// <summary>Current atlas width.</summary>
        int Width { get; }
        
        /// <summary>Current atlas height.</summary>
        int Height { get; }
        
        /// <summary>
        /// Initialize the algorithm with the given dimensions.
        /// </summary>
        void Initialize(int width, int height);
        
        /// <summary>
        /// Try to pack a rectangle of the given size.
        /// </summary>
        /// <param name="width">Width of the rectangle to pack.</param>
        /// <param name="height">Height of the rectangle to pack.</param>
        /// <param name="result">The resulting position if successful.</param>
        /// <returns>True if packing was successful.</returns>
        bool TryPack(int width, int height, out RectInt result);
        
        /// <summary>
        /// Mark a rectangle as free (for removal support).
        /// </summary>
        void Free(RectInt rect);
        
        /// <summary>
        /// Resize the packing area. Existing packed rectangles remain valid.
        /// </summary>
        void Resize(int newWidth, int newHeight);
        
        /// <summary>
        /// Clear all packed rectangles and reset to empty state.
        /// </summary>
        void Clear();
        
        /// <summary>
        /// Get the fill ratio (0-1) of how much space is used.
        /// </summary>
        float GetFillRatio();
    }
}
