using System;
using UnityEngine;

namespace Yoji.EditorDebug.Ui
{
    /// <summary>Mouse button state for one frame of a scripted pointer.</summary>
    public enum ScriptedPointerPhase
    {
        /// <summary>Button up and not just released.</summary>
        None,
        /// <summary>Pressed this frame. Lasts exactly one frame.</summary>
        Down,
        /// <summary>Still pressed.</summary>
        Held,
        /// <summary>Released this frame. Lasts exactly one frame.</summary>
        Up,
    }

    /// <summary>What the game should read from its pointer for one player frame.</summary>
    /// Positions are Unity screen pixels (bottom-left origin), as Input.mousePosition.
    public struct ScriptedPointerFrame
    {
        public ScriptedPointerPhase Phase;
        public Vector2 Position;
        /// <summary>Wheel delta for this frame in Input.mouseScrollDelta units (one notch = 1).</summary>
        public Vector2 Scroll;
        /// <summary>Touches for this frame; empty when the gesture is mouse-only.</summary>
        public Touch[] Touches;
    }

    /// <summary>
    /// A game's scripted pointer, driven one frame at a time by /ui/act world targets.
    /// The package does not know the game's input layer: the game implements this (Editor or development code only)
    /// and registers it with <see cref="ScriptedPointerRegistry"/>.
    /// </summary>
    public interface IScriptedPointer
    {
        /// <summary>Take over the game's pointer reads. Clears any hit reported by a previous gesture.</summary>
        void Begin();

        /// <summary>Called once per player frame, between frames, with the state the next frame reads.</summary>
        void Apply(ScriptedPointerFrame frame);

        /// <summary>Hand the pointer back to the real input.</summary>
        void End();

        /// <summary>
        /// True when the game reported what its own tap logic hit since <see cref="Begin"/>;
        /// <paramref name="hit"/> may then be null (the game hit nothing). False when the game reported nothing.
        /// </summary>
        bool TryGetReportedHit(out GameObject hit);
    }

    /// <summary>The one scripted pointer /ui/act drives. Main thread only.</summary>
    public static class ScriptedPointerRegistry
    {
        private static IScriptedPointer s_Current;

        public static IScriptedPointer Current => s_Current;

        /// <summary>Registers the game's pointer, replacing any previous one.</summary>
        public static void Register(IScriptedPointer pointer)
        {
            s_Current = pointer ?? throw new ArgumentNullException(nameof(pointer));
        }

        /// <summary>Removes the pointer if it is the registered one.</summary>
        public static void Unregister(IScriptedPointer pointer)
        {
            if (ReferenceEquals(s_Current, pointer))
                s_Current = null;
        }
    }
}