using UnityEngine;

namespace SemanticVR.Player
{
    /// <summary>
    /// Device-independent player input. <see cref="PlayerController"/> only talks to this interface, so desktop
    /// (keyboard + mouse), scripted tests and a later Google Cardboard implementation (gaze/button walking, head pose
    /// driving the camera) are interchangeable. Nothing in the colour-processing pipeline depends on it.
    /// </summary>
    public interface IPlayerInput
    {
        /// <summary>Planar move request, x = strafe right, y = forward, each -1..1.</summary>
        Vector2 Move { get; }
        /// <summary>View rotation this frame in degrees: x = yaw (right positive), y = pitch (up positive). Zero for head-tracked devices.</summary>
        Vector2 Look { get; }
        bool Sprint { get; }
        /// <summary>False while a menu has the pointer; the controller then ignores Move and Look.</summary>
        bool Active { get; }
    }
}
