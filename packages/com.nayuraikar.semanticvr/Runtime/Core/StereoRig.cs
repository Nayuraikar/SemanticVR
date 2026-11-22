using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace SemanticVR.Core
{
    /// <summary>
    /// Laptop-friendly stereo rig. Creates a Head transform with two eye cameras (IPD apart), each with an
    /// off-axis (asymmetric) frustum converging on a virtual screen plane, rendered side by side in the Game view
    /// (left eye = viewport 0..0.5, right eye = 0.5..1). Tab (via DebugViewController) toggles mono, which uses a
    /// single full-window centre camera. Mouse-look + WASD / arrow movement via the new Input System.
    /// </summary>
    [DisallowMultipleComponent]
    public class StereoRig : MonoBehaviour
    {
        [Header("Stereo")]
        [Tooltip("Inter-pupillary distance in metres.")] public float ipd = 0.064f;
        [Tooltip("Distance (m) at which left/right images coincide (zero parallax).")] public float convergenceDistance = 2.5f;
        [Tooltip("Vertical field of view (degrees) of the virtual screen plane.")] [Range(30f, 120f)] public float verticalFov = 60f;
        public float nearClip = 0.05f;
        public float farClip = 300f;
        public bool stereo = true;

        [Header("Head")]
        public float eyeHeight = 1.6f;
        [Tooltip("Head transform (pitch). Created as a child if empty.")] public Transform head;
        [Tooltip("Initial downward look angle (degrees).")] public float initialPitch = 6f;

        [Header("Locomotion")]
        public float moveSpeed = 3.0f;
        public float sprintMultiplier = 2.0f;
        [Tooltip("Degrees per mouse pixel.")] public float lookSensitivity = 0.12f;
        public bool enableLocomotion = true;

        public Camera LeftEye { get; private set; }
        public Camera RightEye { get; private set; }
        public Transform Head => head;

        float _yaw, _pitch;

        void Awake()
        {
            if (head == null)
            {
                var existing = transform.Find("Head");
                if (existing != null) head = existing;
                else
                {
                    head = new GameObject("Head").transform;
                    head.SetParent(transform, false);
                }
            }
            head.localPosition = new Vector3(0f, eyeHeight, 0f);
            _yaw = transform.eulerAngles.y;
            _pitch = initialPitch;
            head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            LeftEye = GetOrCreateEye("LeftEye", 0);
            RightEye = GetOrCreateEye("RightEye", 1);
            if (LeftEye.GetComponent<AudioListener>() == null) LeftEye.gameObject.AddComponent<AudioListener>();
            LeftEye.tag = "MainCamera";
            ApplyStereoState();
        }

        Camera GetOrCreateEye(string eyeName, int depth)
        {
            Transform t = head.Find(eyeName);
            if (t == null)
            {
                t = new GameObject(eyeName).transform;
                t.SetParent(head, false);
            }
            var cam = t.GetComponent<Camera>();
            if (cam == null) cam = t.gameObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.depth = depth;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            return cam;
        }

        void Update()
        {
            if (enableLocomotion) HandleLocomotion();
        }

        void LateUpdate()
        {
            ApplyStereoState();
        }

        void HandleLocomotion()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (mouse != null)
            {
                if (Cursor.lockState != CursorLockMode.Locked && mouse.leftButton.wasPressedThisFrame && Application.isFocused)
                    SetCursorLocked(true);
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    _yaw += d.x * lookSensitivity;
                    _pitch = Mathf.Clamp(_pitch - d.y * lookSensitivity, -85f, 85f);
                }
            }
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetCursorLocked(false);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            if (kb == null) return;
            // NOTE: D is reserved for "deutan" in DebugViewController, so strafe-right is the Right arrow.
            float fwd = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f);
            float right = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
            var move = new Vector3(right, 0f, fwd);
            if (move.sqrMagnitude > 1f) move.Normalize();
            float speed = moveSpeed * (kb.leftShiftKey.isPressed ? sprintMultiplier : 1f);
            transform.position += transform.rotation * move * (speed * Time.unscaledDeltaTime);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Set look direction directly (tests / scripted cameras).</summary>
        public void SetYawPitch(float yaw, float pitch)
        {
            _yaw = yaw; _pitch = Mathf.Clamp(pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (head != null) head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        /// <summary>Re-applies eye placement, viewport rects and projection (runs every LateUpdate; call manually after retargeting cameras).</summary>
        public void Apply() => ApplyStereoState();

        void ApplyStereoState()
        {
            if (LeftEye == null || RightEye == null) return;
            head.localPosition = new Vector3(0f, eyeHeight, 0f);

            RightEye.enabled = stereo;
            if (stereo)
            {
                LeftEye.rect = new Rect(0f, 0f, 0.5f, 1f);
                RightEye.rect = new Rect(0.5f, 0f, 0.5f, 1f);
                SetEye(LeftEye, -0.5f * ipd);
                SetEye(RightEye, +0.5f * ipd);
            }
            else
            {
                LeftEye.rect = new Rect(0f, 0f, 1f, 1f);
                SetEye(LeftEye, 0f);
            }
        }

        void SetEye(Camera cam, float eyeX)
        {
            cam.transform.localPosition = new Vector3(eyeX, 0f, 0f);
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = nearClip;
            cam.farClipPlane = farClip;
            cam.projectionMatrix = OffAxisProjection(eyeX, cam.pixelWidth, cam.pixelHeight);
        }

        /// <summary>
        /// Asymmetric frustum for an eye offset <paramref name="eyeX"/> (head space) looking through a virtual
        /// screen plane of the given vertical FOV placed <see cref="convergenceDistance"/> in front of the head.
        /// </summary>
        public Matrix4x4 OffAxisProjection(float eyeX, int pixelWidth, int pixelHeight)
        {
            float aspect = pixelHeight > 0 ? (float)pixelWidth / pixelHeight : 1f;
            float d = Mathf.Max(0.01f, convergenceDistance);
            float halfH = d * Mathf.Tan(0.5f * verticalFov * Mathf.Deg2Rad);
            float halfW = halfH * aspect;
            float k = nearClip / d;
            return Matrix4x4.Frustum((-halfW - eyeX) * k, (halfW - eyeX) * k, -halfH * k, halfH * k, nearClip, farClip);
        }
    }
}
