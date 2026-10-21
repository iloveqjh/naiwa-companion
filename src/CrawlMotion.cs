// SPDX-License-Identifier: MIT
using System;
using System.Drawing;

namespace NaiwaCompanion
{
    // Fractional coordinates avoid rounding slow movement down to zero at 60 Hz.
    internal sealed class CrawlMotion
    {
        internal double X, Y, Time;
        internal int Direction = 1;
        private double previous = -1;
        internal void Reset(Point position) { X = position.X; Y = position.Y; previous = -1; }
        internal Point Advance(double now, Rectangle area, Size size, double speed, bool paused)
        {
            double dt = previous < 0 ? 0 : Math.Max(0, Math.Min(.08, now - previous)); previous = now;
            if (paused) return new Point((int)Math.Round(X), (int)Math.Round(Y));
            Time += dt;
            double left = area.Left, right = Math.Max(left, area.Right - size.Width);
            Y = Math.Max(area.Top, Math.Min(Y, Math.Max(area.Top, area.Bottom - size.Height)));
            if (right > left)
            {
                X = Math.Max(left, Math.Min(X, right)) + Direction * speed * dt;
                // Reflect overshoot so contact with an edge does not add a pause.
                while (X < left || X > right)
                {
                    if (X > right) { X = right - (X - right); Direction = -1; }
                    if (X < left) { X = left + (left - X); Direction = 1; }
                }
            }
            else X = left;
            return new Point((int)Math.Round(X), (int)Math.Round(Y));
        }
    }
}
