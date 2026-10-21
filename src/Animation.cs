// SPDX-License-Identifier: MIT
using System;
using System.Drawing;

namespace NaiwaCompanion
{
    // All animation uses elapsed time, rather than assuming each timer tick arrived.
    internal sealed class Animation
    {
        internal double Time, GazeX, GazeY, Tilt;
        internal double LaughAt = -1, BlinkAt = -1, PokeAt = -1, DropAt = -1, StandAt = -1;
        internal bool Resting, Dragging;
        internal bool Crawling, FacingLeft;
        internal double CrawlTime;
        internal readonly HeadPressMotion Head = new HeadPressMotion();
        private double lastTime = -1, nextBlink = 4.5, tiltSpeed, desiredTilt, lastDrag;
        private readonly Random random = new Random(3721);
        internal bool Laughing { get { return LaughAt >= 0; } }
        internal bool Blinking { get { return BlinkAt >= 0; } }
        internal double LaughTime { get { return Math.Max(0, Time - LaughAt); } }
        internal bool Active { get { return Head.Active || Laughing || Blinking || Dragging || PokeAt >= 0 || DropAt >= 0 || StandAt >= 0 || Math.Abs(Tilt) > .01; } }

        internal static PointF GazeTarget(PointF pointer, SizeF size, bool onBody, bool enabled, PointF? origin = null)
        {
            if (onBody || !enabled) return PointF.Empty;
            PointF eye = origin ?? new PointF(size.Width * .431f, size.Height * .185f);
            double dx = (pointer.X - eye.X) / Math.Max(180, size.Width * 1.8);
            double dy = (pointer.Y - eye.Y) / Math.Max(180, size.Height * 1.3);
            // A soft bound avoids a sudden stop when the pointer moves far away.
            return new PointF((float)Math.Tanh(dx), (float)Math.Tanh(dy));
        }

        internal void Advance(double now, PointF target, double clipLength, double blinkLength, bool allowBlink = true)
        {
            double dt = lastTime < 0 ? 0 : Math.Max(0, Math.Min(.08, now - lastTime));
            lastTime = now; Time = now;
            Head.Advance(now);
            if (Head.Reacting) target = PointF.Empty;
            double ease = 1 - Math.Exp(-dt * 12);
            GazeX += (target.X - GazeX) * ease; GazeY += (target.Y - GazeY) * ease;
            if (Laughing && LaughTime >= clipLength) { LaughAt = -1; Resting = true; }
            if (Blinking && now - BlinkAt >= blinkLength) { BlinkAt = -1; PlanBlink(); }
            if (PokeAt >= 0 && now - PokeAt > 1.05) PokeAt = -1;
            if (DropAt >= 0 && now - DropAt > .85) DropAt = -1;
            if (StandAt >= 0 && now - StandAt > .26) StandAt = -1;
            if (allowBlink && !Head.Active && !Laughing && !Resting && !Dragging && PokeAt < 0 && DropAt < 0 && StandAt < 0 && !Blinking && now >= nextBlink) BlinkAt = now;
            if (!Dragging || now - lastDrag > .09) desiredTilt = 0;
            int parts = Math.Max(1, (int)Math.Ceiling(dt / .006));
            for (int i = 0; i < parts; i++)
            {
                double step = dt / parts;
                tiltSpeed += ((desiredTilt - Tilt) * 190 - tiltSpeed * 28) * step;
                Tilt += tiltSpeed * step;
            }
            if (!Dragging && Math.Abs(Tilt) < .015 && Math.Abs(tiltSpeed) < .04) { Tilt = 0; tiltSpeed = 0; }
        }

        private void PlanBlink() { nextBlink = Time + 4 + random.NextDouble() * 4; }
        private void ClearSmall() { BlinkAt = PokeAt = DropAt = StandAt = -1; Tilt = tiltSpeed = desiredTilt = 0; PlanBlink(); }
        internal bool StartLaugh(double now)
        {
            if (Laughing) return false;
            Head.Cancel();
            Time = now; ClearSmall(); Resting = Dragging = false; LaughAt = now; return true;
        }
        internal void Stop(double now) { Head.Cancel(); Time = now; LaughAt = -1; Resting = Dragging = false; ClearSmall(); }
        internal bool BeginHeadPress(double now)
        {
            if (Laughing || Resting || Dragging) return false;
            Time = now; ClearSmall(); Head.Begin(now); return true;
        }
        internal void DemoHeadPress(double now)
        { Stop(now); Head.Demo(now); }
        internal void Stand(double now) { Stop(now); StandAt = now; }
        internal bool Poke(double now, PointF p, Size size, RectangleF? area = null)
        {
            RectangleF belly = area ?? new RectangleF(size.Width * .255f, size.Height * .38f, size.Width * .49f, size.Height * .38f);
            double x = (p.X - belly.X - belly.Width / 2) / (belly.Width / 2), y = (p.Y - belly.Y - belly.Height / 2) / (belly.Height / 2);
            if (x * x + y * y > 1 || Head.Active || Laughing || Resting || Dragging) return false;
            Time = now; ClearSmall(); PokeAt = now; return true;
        }
        internal void BeginDrag(double now) { Stop(now); Dragging = true; lastDrag = now; }
        internal void DragVelocity(double now, double pixels, double dt)
        { desiredTilt = Math.Max(-6.5, Math.Min(6.5, pixels / Math.Max(.008, dt) * .011)); lastDrag = now; }
        internal void Release(double now) { Dragging = false; desiredTilt = 0; Time = now; DropAt = now; PlanBlink(); }
        internal double BellyAmount
        {
            get { double t = Time - PokeAt; if (PokeAt < 0) return 0; return t < .11 ? .45 * Math.Sin(t / .11 * Math.PI / 2) : .45 * Math.Exp(-5.5 * (t - .11)) * Math.Cos(20 * (t - .11)); }
        }
        internal double DropAmount { get { double t = Time - DropAt; return DropAt < 0 ? 0 : .045 * Math.Exp(-6 * t) * Math.Sin(21 * t); } }
    }

    internal sealed class ClickTracker
    {
        internal bool Held, Moved;
        internal Point Origin;
        private Point previous;
        private double previousAt = -10;
        internal void Down(Point p) { Held = true; Moved = false; Origin = p; }
        internal void Move(Point p) { if (Held && (Math.Abs(p.X - Origin.X) > 5 || Math.Abs(p.Y - Origin.Y) > 5)) Moved = true; }
        internal bool Up(Point p, double time, int interval, Size area)
        {
            Held = false;
            if (Moved) { previousAt = -10; return false; }
            bool twice = time - previousAt <= interval / 1000.0 && Math.Abs(p.X - previous.X) <= area.Width && Math.Abs(p.Y - previous.Y) <= area.Height;
            previousAt = twice ? -10 : time; previous = p; return twice;
        }
        internal void Cancel() { Held = Moved = false; previousAt = -10; }
    }
}
