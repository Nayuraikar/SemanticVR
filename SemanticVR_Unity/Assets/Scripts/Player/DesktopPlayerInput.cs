using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SemanticVR.Player
{
    /// <summary>
    /// Keyboard and mouse input (WASD / arrows, Shift to run, mouse look) using the Input System package directly, no
    /// actions asset required. Owns the cursor: locked while walking, free while the settings menu is open
    /// (Tab or Esc toggles; clicking the world resumes walking).
    /// </summary>
    public sealed class DesktopPlayerInput : MonoBehaviour, IPlayerInput
    {
        [Tooltip("Degrees of rotation per pixel of mouse movement.")]
        [Range(0.02f, 0.5f)] public float sensitivity = 0.1f;
        public bool invertY = false;
        public bool startInMenu = false;

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool Active => !MenuOpen;
        public bool MenuOpen { get; private set; }
        public event Action<bool> MenuChanged;

        // TEMP DIAGNOSTICS: once-per-second state dump to the Console. Remove after the movement issue is confirmed fixed.
        public bool debugLog = true;
        float nextDebugLog;
        // True once the cursor has actually reported Locked while walking. The "cursor got unlocked" fallback only applies after this.
        bool lockAcquired;

        void Start() { SetMenu(startInMenu, true); }

        public void SetMenu(bool open, bool force = false)
        {
            if (!force && open == MenuOpen) return;
            MenuOpen = open;
            lockAcquired = false;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open) { Move = Vector2.zero; Look = Vector2.zero; Sprint = false; }
            MenuChanged?.Invoke(open);
        }

        void OnApplicationFocus(bool focus) { if (!focus && !MenuOpen && lockAcquired) SetMenu(true); }

        void Update()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (kb != null && (kb.tabKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame)) SetMenu(!MenuOpen);
            if (debugLog && Time.unscaledTime >= nextDebugLog)
            {
                nextDebugLog = Time.unscaledTime + 1f;
                Debug.Log($"[DesktopPlayerInput] MenuOpen={MenuOpen} lockState={Cursor.lockState} lockAcquired={lockAcquired} focused={Application.isFocused} " +
                          $"keyboard={(kb != null)} mouse={(mouse != null)} W={(kb != null && kb.wKey.isPressed)} move={Move}");
            }
            if (MenuOpen)
            {
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUi && Application.isFocused) SetMenu(false);
                return;
            }
            // Editor Esc / external unlock while walking: fall back to the menu instead of looking with a free cursor.
            // Only after the lock has really been acquired; at Editor startup the Game view may not be focused yet, so the
            // lock is pending and we keep re-requesting it (a click in the focused Game view completes it).
            bool locked = Cursor.lockState == CursorLockMode.Locked;
            if (locked) lockAcquired = true;
            else if (lockAcquired) { SetMenu(true); return; }
            else
            {
                if (Application.isFocused) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
                if (mouse != null && mouse.leftButton.wasPressedThisFrame) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
            }

            Vector2 m = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) m.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) m.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) m.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) m.x -= 1;
                Sprint = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            Move = Vector2.ClampMagnitude(m, 1f);
            Vector2 d = mouse != null && locked ? mouse.delta.ReadValue() * sensitivity : Vector2.zero;
            d = Vector2.ClampMagnitude(d, 45f);   // swallow the jump when the cursor is (re)locked
            Look = new Vector2(d.x, invertY ? -d.y : d.y);
        }
    }
}
