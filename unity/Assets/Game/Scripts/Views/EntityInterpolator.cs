using System.Collections.Generic;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>One authoritative position sample stamped with a receive-time clock.</summary>
    public readonly struct PositionSample
    {
        public readonly float Time;
        public readonly float X;
        public readonly float Y;

        public PositionSample(float time, float x, float y)
        {
            Time = time;
            X = x;
            Y = y;
        }
    }

    /// <summary>
    /// Pure snapshot-interpolation math used by <see cref="EntityInterpolator"/> and EditMode tests.
    /// Renders at a delayed time between buffered samples; never extrapolates past the newest sample.
    /// </summary>
    public static class EntityInterpolation
    {
        public readonly struct Result
        {
            public readonly float X;
            public readonly float Y;
            public readonly bool Snapped;
            public readonly bool Valid;

            public Result(float x, float y, bool snapped, bool valid)
            {
                X = x;
                Y = y;
                Snapped = snapped;
                Valid = valid;
            }
        }

        /// <summary>
        /// Evaluate position at <paramref name="renderTime"/> from ascending-time samples.
        /// Holds the newest sample when <paramref name="renderTime"/> is at/after it (no extrapolation).
        /// <paramref name="pruneBeforeIndex"/> is the first sample index that must be retained.
        /// </summary>
        public static Result Evaluate(
            IReadOnlyList<PositionSample> samples,
            float renderTime,
            float snapDistance,
            out int pruneBeforeIndex)
        {
            pruneBeforeIndex = 0;

            if (samples == null || samples.Count == 0)
            {
                return new Result(0f, 0f, false, false);
            }

            if (samples.Count == 1)
            {
                var only = samples[0];
                pruneBeforeIndex = 0;
                return new Result(only.X, only.Y, true, true);
            }

            var lastIndex = samples.Count - 1;
            var last = samples[lastIndex];

            if (renderTime >= last.Time)
            {
                pruneBeforeIndex = lastIndex;
                return new Result(last.X, last.Y, false, true);
            }

            var first = samples[0];
            if (renderTime <= first.Time)
            {
                pruneBeforeIndex = 0;
                return new Result(first.X, first.Y, true, true);
            }

            var right = 1;
            while (right < samples.Count && samples[right].Time < renderTime)
            {
                right++;
            }

            if (right >= samples.Count)
            {
                pruneBeforeIndex = lastIndex;
                return new Result(last.X, last.Y, false, true);
            }

            var left = right - 1;
            pruneBeforeIndex = left;

            var a = samples[left];
            var b = samples[right];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var distanceSq = dx * dx + dy * dy;
            var snapDistanceSq = snapDistance * snapDistance;

            if (distanceSq > snapDistanceSq)
            {
                return new Result(b.X, b.Y, true, true);
            }

            var dt = b.Time - a.Time;
            if (dt <= Mathf.Epsilon)
            {
                return new Result(b.X, b.Y, true, true);
            }

            var t = Mathf.Clamp01((renderTime - a.Time) / dt);
            return new Result(a.X + dx * t, a.Y + dy * t, false, true);
        }

        /// <summary>
        /// Remove consumed samples before <paramref name="pruneBeforeIndex"/>.
        /// Returns how many entries were removed.
        /// </summary>
        public static int Prune(List<PositionSample> samples, int pruneBeforeIndex)
        {
            if (samples == null || pruneBeforeIndex <= 0 || samples.Count == 0)
            {
                return 0;
            }

            var removeCount = Mathf.Min(pruneBeforeIndex, samples.Count - 1);
            if (removeCount <= 0)
            {
                return 0;
            }

            samples.RemoveRange(0, removeCount);
            return removeCount;
        }

        /// <summary>
        /// Drop the oldest samples so the buffer never exceeds <paramref name="maxCount"/>.
        /// Keeps the newest entries (including any pair that can still bracket a delayed render time).
        /// </summary>
        public static int Cap(List<PositionSample> samples, int maxCount)
        {
            if (samples == null || maxCount < 1 || samples.Count <= maxCount)
            {
                return 0;
            }

            var removeCount = samples.Count - maxCount;
            samples.RemoveRange(0, removeCount);
            return removeCount;
        }
    }

    /// <summary>
    /// Buffers timestamped replicated positions and renders at <c>now - renderDelay</c>.
    /// Uses <see cref="Time.time"/> so receive stamps and the render clock share the scaled game clock
    /// (Editor/game pause freezes interpolation instead of aging the buffer under wall time).
    /// Holds the newest sample when no future sample exists yet — safer for a sample than extrapolating.
    /// Preserves the transform Z (2D URP).
    /// </summary>
    public sealed class EntityInterpolator : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Seconds behind real time to render. ~0.1s is two server ticks at 20 Hz.")]
        private float renderDelay = 0.1f;

        [SerializeField]
        [Tooltip("World-units jump between bracketing samples that forces a snap (teleport).")]
        private float snapDistance = 2f;

        [SerializeField]
        [Tooltip("If no sample newer than this age (seconds) exists, snap to the newest sample.")]
        private float staleAfter = 0.35f;

        /// <summary>
        /// Hard ceiling on buffered samples. Bounds growth when LateUpdate cannot prune
        /// (inactive GameObject still receiving sync callbacks, or timeScale == 0 with live traffic).
        /// Sized above anything a 0.1s render delay at 20 Hz plus jitter can need.
        /// </summary>
        public const int MaxBufferedSamples = 32;

        private readonly List<PositionSample> samples = new(16);
        private bool hasInitialSample;

        public float RenderDelay => renderDelay;
        public float SnapDistance => snapDistance;
        public float StaleAfter => staleAfter;
        public int SampleCount => samples.Count;

        public void Clear()
        {
            samples.Clear();
            hasInitialSample = false;
        }

        public void PushSample(float x, float y)
        {
            PushSample(Time.time, x, y);
        }

        public void PushSample(float time, float x, float y)
        {
            if (samples.Count > 0)
            {
                var last = samples[samples.Count - 1];
                if (time < last.Time)
                {
                    time = last.Time;
                }
            }

            samples.Add(new PositionSample(time, x, y));
            EntityInterpolation.Cap(samples, MaxBufferedSamples);

            if (!hasInitialSample)
            {
                hasInitialSample = true;
                ApplyPosition(x, y);
            }
        }

        private void LateUpdate()
        {
            if (!hasInitialSample || samples.Count == 0)
            {
                return;
            }

            var now = Time.time;
            var newest = samples[samples.Count - 1];
            if (now - newest.Time > staleAfter)
            {
                ApplyPosition(newest.X, newest.Y);
                EntityInterpolation.Prune(samples, samples.Count - 1);
                return;
            }

            var renderTime = now - renderDelay;
            var result = EntityInterpolation.Evaluate(samples, renderTime, snapDistance, out var pruneBefore);
            if (!result.Valid)
            {
                return;
            }

            ApplyPosition(result.X, result.Y);
            EntityInterpolation.Prune(samples, pruneBefore);
        }

        private void ApplyPosition(float x, float y)
        {
            var p = transform.position;
            p.x = x;
            p.y = y;
            transform.position = p;
        }

        private void OnDisable()
        {
            Clear();
        }
    }
}
