using UnityEngine;

namespace SemanticVR.Core
{
    /// <summary>Gaze = head forward vector from the head transform (default: the StereoRig's head).</summary>
    public class HeadPoseGazeSource : MonoBehaviour, IGazeSource
    {
        [Tooltip("Head transform. If empty, the StereoRig on this object (or its parents) is used, else this transform.")]
        public Transform head;

        public bool TryGetGazeRay(out Ray ray)
        {
            Transform h = head;
            if (h == null)
            {
                var rig = GetComponentInParent<StereoRig>();
                h = rig != null ? rig.Head : transform;
            }
            ray = new Ray(h.position, h.forward);
            return true;
        }
    }
}
