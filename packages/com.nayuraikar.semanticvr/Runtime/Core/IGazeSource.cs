using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>
    /// Where the user is looking. Head pose today (<see cref="HeadPoseGazeSource"/>);
    /// an eye tracker can implement the same interface later.
    /// </summary>
    public interface IGazeSource
    {
        /// <summary>World-space gaze ray. Returns false when no valid gaze is available.</summary>
        bool TryGetGazeRay(out Ray ray);
    }
}
