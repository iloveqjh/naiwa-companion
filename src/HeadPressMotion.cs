// SPDX-License-Identifier: MIT
using System;
using System.Drawing;

namespace NaiwaCompanion
{
    internal sealed class HeadPressMotion
    {
        internal bool Held;
        internal double Amount, Velocity;
        internal double ReactionDuration = .85;
        private double target, previous = -1, peak, released = -1, demoAt = -1;
        internal bool Active { get { return Held || Math.Abs(Amount) > .0001 || Math.Abs(Velocity) > .001 || (released >= 0 && previous < released + .32 + ReactionDuration + .10); } }
        internal double ReactionTime { get { return released < 0 || peak < .045 ? -1 : previous - released - .32; } }
        internal bool Reacting { get { return ReactionTime >= 0 && ReactionTime < ReactionDuration; } }
        internal double ReactionWeight
        {
            get
            {
                if (released < 0 || peak < .045) return 0;
                double t = ReactionTime;
                if (t < 0 || t >= ReactionDuration) return 0;
                double fade = Math.Min(1, Math.Min(t / .09, (ReactionDuration - t) / .13));
                return fade * fade * (3 - 2 * fade);
            }
        }
        internal static bool DownwardGesture(Point start, Point current)
        { return current.Y - start.Y >= 6 && current.Y - start.Y > Math.Abs(current.X - start.X) * 1.15; }
        internal void Begin(double now)
        { Held = true; target = 0; previous = now; peak = Math.Max(0, Amount); released = demoAt = -1; }
        internal void Pull(double pixels, double bodyHeight)
        { if (Held) target = .28 * Math.Tanh(Math.Max(0, pixels) / Math.Max(30, bodyHeight * .24)); }
        internal void Release(double now)
        { if (!Held) return; Held = false; target = 0; released = peak >= .045 ? now : -1; demoAt = -1; }
        internal void Demo(double now) { Begin(now); demoAt = now; }
        internal void Cancel()
        { Held = false; target = Amount = Velocity = peak = 0; previous = released = demoAt = -1; }
        internal void Advance(double now)
        {
            double dt = previous < 0 ? 0 : Math.Max(0, Math.Min(.08, now - previous)); previous = now;
            if (demoAt >= 0)
            {
                double t = now - demoAt;
                if (t >= .76) Release(now);
                else Pull(100 * Math.Min(1, Math.Max(0, t) / .3), 300);
            }
            int count = Math.Max(1, (int)Math.Ceiling(dt / .004));
            for (int n = 0; n < count; n++)
            {
                double h = dt / count;
                double stiffness = Held ? 480 : 240, damping = Held ? 40 : 12.5;
                Velocity += ((target - Amount) * stiffness - Velocity * damping) * h;
                Amount += Velocity * h;
            }
            if (Held) peak = Math.Max(peak, Amount);
            if (!Held && Math.Abs(Amount) < .0003 && Math.Abs(Velocity) < .004) Amount = Velocity = 0;
        }
    }
}
