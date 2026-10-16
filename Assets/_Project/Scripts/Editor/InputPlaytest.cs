using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MurderMystery.Gameplay;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace MurderMystery.EditorTools
{
    /// <summary>
    /// Plays the local player with simulated keys in Play mode, the way a tester would, and records what the
    /// body and camera did. For checks that need the real controller (ducking, jumping, sills) rather than
    /// the walking probe. Example: <c>Run(new Vector3(8,0,-15), 0, "W:2 Space W:1.5 Wait:0.5")</c>, then read
    /// <see cref="Report"/> once <see cref="Done"/>. Steps: <c>Key:seconds</c> holds keys (W, A, S, D, Shift,
    /// combined with +), a bare key taps it, <c>Wait:seconds</c> idles, <c>Yaw:degrees</c> turns.
    /// </summary>
    public static class InputPlaytest
    {
        public static bool Done { get; private set; } = true;
        public static string Report { get; private set; } = string.Empty;

        private struct Step { public Key[] Keys; public float Seconds; public float Yaw; public bool Turn; }

        private static Queue<Step> steps;
        private static Step current;
        private static double stepEnd;
        private static double nextSample;
        private static FirstPersonController player;
        private static StringBuilder log;
        private static InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
        private static InputSettings.BackgroundBehavior savedBackground;
        private static float minHeight, minEye, maxY, startY;

        public static string Run(Vector3 position, float yaw, string script)
        {
            if (!EditorApplication.isPlaying) return "not playing";
            PrototypeBootstrap.SpawnOffline();
            player = Object.FindFirstObjectByType<FirstPersonController>();
            if (player == null) return "no player";

            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(position + Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
            cc.enabled = true;
            player.InputEnabled = true;

            steps = new Queue<Step>(Parse(script));
            savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            savedBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            log = new StringBuilder();
            minHeight = minEye = float.MaxValue;
            maxY = startY = player.transform.position.y;
            Done = false;
            Report = string.Empty;
            current = new Step { Keys = new Key[0], Seconds = 0.3f };   // settle first
            stepEnd = EditorApplication.timeSinceStartup + current.Seconds;
            nextSample = 0;
            Press();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            return "started";
        }

        private static IEnumerable<Step> Parse(string script)
        {
            foreach (var token in script.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');
                var seconds = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : 0.12f;
                if (parts[0] == "Wait") yield return new Step { Keys = new Key[0], Seconds = seconds };
                else if (parts[0] == "Yaw") yield return new Step { Keys = new Key[0], Seconds = 0.05f, Yaw = seconds, Turn = true };
                else
                {
                    var keys = parts[0].Split('+').Select(k => k == "Shift" ? Key.LeftShift : (Key)System.Enum.Parse(typeof(Key), k)).ToArray();
                    yield return new Step { Keys = keys, Seconds = seconds };
                    // A tap is released before the next step starts.
                    if (parts.Length == 1) yield return new Step { Keys = new Key[0], Seconds = 0.05f };
                }
            }
        }

        private static void Press(bool turn = true)
        {
            if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
            if (Mouse.current == null) InputSystem.AddDevice<Mouse>();
            // With the editor unfocused the input system may have switched the devices off.
            if (!Keyboard.current.enabled) InputSystem.EnableDevice(Keyboard.current);
            if (!Mouse.current.enabled) InputSystem.EnableDevice(Mouse.current);
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(current.Keys));
            if (turn && current.Turn) player.transform.Rotate(0f, current.Yaw, 0f);
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || player == null)
            {
                Finish("play mode ended");
                return;
            }
            var now = EditorApplication.timeSinceStartup;
            var cc = player.GetComponent<CharacterController>();
            var eye = player.transform.Find("CameraPivot").localPosition.y;
            minHeight = Mathf.Min(minHeight, cc.height);
            minEye = Mathf.Min(minEye, eye);
            maxY = Mathf.Max(maxY, player.transform.position.y);
            if (now >= nextSample)
            {
                nextSample = now + 0.1;
                log.AppendLine($"{player.transform.position.x:F2},{player.transform.position.y:F2},{player.transform.position.z:F2} h={cc.height:F2} eye={eye:F2} g={(cc.isGrounded ? 1 : 0)} keys={string.Join("+", current.Keys)}");
            }
            if (now < stepEnd)
            {
                // Held keys are re-sent: losing focus resets device state.
                if (current.Keys.Length > 0) Press(false);
                return;
            }
            if (steps.Count == 0)
            {
                Finish(null);
                return;
            }
            current = steps.Dequeue();
            stepEnd = now + current.Seconds;
            Press();
        }

        private static void Finish(string error)
        {
            EditorApplication.update -= Tick;
            if (Keyboard.current != null) InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
            InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
            InputSystem.settings.backgroundBehavior = savedBackground;
            var head = error ?? (player == null ? "no player" :
                $"end {player.transform.position:F2} minHeight={minHeight:F2} minEye={minEye:F2} rise={maxY - startY:F2}");
            Report = head + "\n" + log;
            Done = true;
        }
    }
}
