using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GolemSample.Unity.Tests
{
    public sealed class EntityInterpolatorTests
    {
        [Test]
        public void FirstSample_SnapsToThatPosition()
        {
            var samples = new List<PositionSample>
            {
                new(1f, 3f, 4f),
            };

            var result = EntityInterpolation.Evaluate(samples, 0.5f, snapDistance: 2f, out var pruneBefore);

            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.Snapped);
            Assert.AreEqual(3f, result.X, 1e-5f);
            Assert.AreEqual(4f, result.Y, 1e-5f);
            Assert.AreEqual(0, pruneBefore);
        }

        [Test]
        public void BetweenTwoSamples_InterpolatesAtKnownTime()
        {
            var samples = new List<PositionSample>
            {
                new(1.0f, 0f, 0f),
                new(2.0f, 10f, 20f),
            };

            // Asymmetric: t=0.25 so an inverted factor (0.75) would yield 7.5/15 and fail.
            var result = EntityInterpolation.Evaluate(samples, 1.25f, snapDistance: 100f, out var pruneBefore);

            Assert.IsTrue(result.Valid);
            Assert.IsFalse(result.Snapped);
            Assert.AreEqual(2.5f, result.X, 1e-5f);
            Assert.AreEqual(5f, result.Y, 1e-5f);
            Assert.AreEqual(0, pruneBefore);
        }

        [Test]
        public void EqualTimestampSamples_DoNotProduceNaN()
        {
            var samples = new List<PositionSample>
            {
                new(1.0f, 0f, 0f),
                new(1.0f, 4f, 8f),
            };

            var result = EntityInterpolation.Evaluate(samples, 1.0f, snapDistance: 100f, out _);

            Assert.IsTrue(result.Valid);
            Assert.IsFalse(float.IsNaN(result.X));
            Assert.IsFalse(float.IsNaN(result.Y));
            Assert.AreEqual(4f, result.X, 1e-5f);
            Assert.AreEqual(8f, result.Y, 1e-5f);
        }

        [Test]
        public void LargeJump_SnapsToLaterSample()
        {
            var samples = new List<PositionSample>
            {
                new(1.0f, 0f, 0f),
                new(2.0f, 50f, 0f),
            };

            var result = EntityInterpolation.Evaluate(samples, 1.5f, snapDistance: 2f, out _);

            Assert.IsTrue(result.Valid);
            Assert.IsTrue(result.Snapped);
            Assert.AreEqual(50f, result.X, 1e-5f);
            Assert.AreEqual(0f, result.Y, 1e-5f);
        }

        [Test]
        public void Prune_RemovesConsumedSamplesButKeepsBracket()
        {
            var samples = new List<PositionSample>
            {
                new(1.0f, 0f, 0f),
                new(2.0f, 10f, 0f),
                new(3.0f, 20f, 0f),
                new(4.0f, 30f, 0f),
            };

            var result = EntityInterpolation.Evaluate(samples, 3.5f, snapDistance: 100f, out var pruneBefore);

            Assert.IsTrue(result.Valid);
            Assert.AreEqual(25f, result.X, 1e-5f);
            Assert.AreEqual(2, pruneBefore);

            var removed = EntityInterpolation.Prune(samples, pruneBefore);

            Assert.AreEqual(2, removed);
            Assert.AreEqual(2, samples.Count);
            Assert.AreEqual(3.0f, samples[0].Time, 1e-5f);
            Assert.AreEqual(4.0f, samples[1].Time, 1e-5f);
        }

        [Test]
        public void Hold_WhenRenderTimeAtOrPastNewestSample()
        {
            var samples = new List<PositionSample>
            {
                new(1.0f, 0f, 0f),
                new(2.0f, 10f, 0f),
            };

            var result = EntityInterpolation.Evaluate(samples, 2.5f, snapDistance: 100f, out var pruneBefore);

            Assert.IsTrue(result.Valid);
            Assert.IsFalse(result.Snapped);
            Assert.AreEqual(10f, result.X, 1e-5f);
            Assert.AreEqual(0f, result.Y, 1e-5f);
            Assert.AreEqual(1, pruneBefore);

            var removed = EntityInterpolation.Prune(samples, pruneBefore);
            Assert.AreEqual(1, removed);
            Assert.AreEqual(1, samples.Count);
            Assert.AreEqual(2.0f, samples[0].Time, 1e-5f);

            var afterPrune = EntityInterpolation.Evaluate(samples, 2.5f, snapDistance: 100f, out var pruneAfter);
            Assert.IsTrue(afterPrune.Valid);
            Assert.AreEqual(10f, afterPrune.X, 1e-5f);
            Assert.AreEqual(0f, afterPrune.Y, 1e-5f);
            Assert.AreEqual(0, pruneAfter);
        }

        [Test]
        public void Cap_DropsOldestBeyondMaxAndKeepsNewest()
        {
            var samples = new List<PositionSample>();
            const int maxCount = 8;
            for (var i = 0; i < maxCount + 5; i++)
            {
                samples.Add(new PositionSample(i, i * 10f, 0f));
                EntityInterpolation.Cap(samples, maxCount);
            }

            Assert.AreEqual(maxCount, samples.Count);
            Assert.AreEqual(5f, samples[0].Time, 1e-5f);
            Assert.AreEqual(12f, samples[samples.Count - 1].Time, 1e-5f);

            // Bracket for renderTime 10.5 must still be present after capping to newest 8.
            var result = EntityInterpolation.Evaluate(samples, 10.5f, snapDistance: 100f, out var pruneBefore);
            Assert.IsTrue(result.Valid);
            Assert.IsFalse(result.Snapped);
            Assert.AreEqual(105f, result.X, 1e-5f);
            Assert.AreEqual(5, pruneBefore);
        }

        [Test]
        public void EmptyBuffer_IsInvalid()
        {
            var result = EntityInterpolation.Evaluate(new List<PositionSample>(), 1f, 2f, out _);
            Assert.IsFalse(result.Valid);
        }
    }
}
