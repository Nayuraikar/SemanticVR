using UnityEngine;

namespace SemanticVR.Player
{
    /// <summary>
    /// First-person pedestrian controller: yaw rotates this rig, pitch rotates <see cref="head"/> (the camera), movement
    /// uses a CharacterController (collides with the environment, steps up kerbs). Eye height and speeds are realistic
    /// for an adult pedestrian (eye 1.6 m, walk 1.5 m/s, run 3.2 m/s). Input comes from <see cref="IPlayerInput"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public Transform head;
        [Header("Body")]
        public float eyeHeight = 1.6f, bodyHeight = 1.75f, radius = 0.3f;
        [Header("Movement (m/s)")]
        public float walkSpeed = 1.5f, runSpeed = 3.2f, acceleration = 12f, gravity = -12f;
        [Header("View")]
        [Range(40, 89)] public float maxPitch = 80f;
        public bool headBob = true;
        [Range(0f, 0.05f)] public float bobAmplitude = 0.02f;

        public IPlayerInput Input { get; set; }
        public Vector3 Velocity => horizontal;
        public float Speed => horizontal.magnitude;

        CharacterController cc;
        Vector3 horizontal; float vy, yaw, pitch, bobPhase;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            foreach (var mb in GetComponents<MonoBehaviour>()) if (mb is IPlayerInput pi) { Input = pi; break; }
            cc.height = bodyHeight; cc.radius = radius; cc.center = new Vector3(0, bodyHeight * 0.5f + cc.skinWidth, 0);
            cc.stepOffset = 0.3f; cc.slopeLimit = 45f; cc.minMoveDistance = 0f;
        }

        void Start()
        {
            yaw = transform.eulerAngles.y;
            if (head != null) { head.localPosition = new Vector3(0, eyeHeight, 0); pitch = NormalisePitch(head.localEulerAngles.x); }
        }

        static float NormalisePitch(float x) => x > 180f ? x - 360f : x;

        /// <summary>Moves the player (CharacterController safe) and sets the heading.</summary>
        public void Teleport(Vector3 position, float yawDegrees)
        {
            cc.enabled = false; transform.position = position; cc.enabled = true;
            yaw = yawDegrees; pitch = 0; horizontal = Vector3.zero; vy = 0;
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (head != null) head.localRotation = Quaternion.identity;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 move = Vector2.zero; bool sprint = false;
            if (Input != null && Input.Active)
            {
                var look = Input.Look;
                yaw += look.x; pitch = Mathf.Clamp(pitch - look.y, -maxPitch, maxPitch);
                move = Input.Move; sprint = Input.Sprint;
            }
            transform.rotation = Quaternion.Euler(0, yaw, 0);
            if (head != null) head.localRotation = Quaternion.Euler(pitch, 0, 0);

            Vector3 wish = (transform.right * move.x + transform.forward * move.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= sprint ? runSpeed : walkSpeed;
            horizontal = Vector3.MoveTowards(horizontal, wish, acceleration * dt);

            vy = cc.isGrounded ? -2f : vy + gravity * dt;
            cc.Move((horizontal + Vector3.up * vy) * dt);

            if (head != null)
            {
                float s = horizontal.magnitude;
                bobPhase += s * dt * 4.2f;   // ~2 steps per second at walking pace
                float bob = headBob && cc.isGrounded ? Mathf.Abs(Mathf.Sin(bobPhase)) * bobAmplitude * Mathf.Clamp01(s / walkSpeed) : 0f;
                head.localPosition = new Vector3(0, eyeHeight + bob, 0);
            }
        }
    }
}
