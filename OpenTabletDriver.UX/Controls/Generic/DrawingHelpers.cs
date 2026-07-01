using System;
using Eto.Drawing;

namespace OpenTabletDriver.UX.Controls.Generic
{
    internal static class DrawingHelpers
    {
        // Eto.Mac's Graphics.FillPath renders a rounded-rect path as a hollow outline in the
        // signed single-file/self-contained build (it fills fine in a normal debug build), which
        // made every card and the sidebar selection look like a jagged blue polygon. FillRectangle
        // and FillEllipse DO fill correctly in that build, so compose the rounded rectangle from
        // two rectangles plus four corner circles instead of filling a path.
        public static void FillRoundedRect(Graphics g, Color color, RectangleF r, float radius)
        {
            radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
            if (radius <= 0)
            {
                g.FillRectangle(color, r);
                return;
            }

            float d = radius * 2f;

            // Plus-shaped body: a full-height band inset horizontally, and a full-width band inset
            // vertically. Together they cover everything except the four corner squares.
            g.FillRectangle(color, new RectangleF(r.X + radius, r.Y, r.Width - d, r.Height));
            g.FillRectangle(color, new RectangleF(r.X, r.Y + radius, r.Width, r.Height - d));

            // Round the four corner squares.
            g.FillEllipse(color, r.X, r.Y, d, d);
            g.FillEllipse(color, r.Right - d, r.Y, d, d);
            g.FillEllipse(color, r.X, r.Bottom - d, d, d);
            g.FillEllipse(color, r.Right - d, r.Bottom - d, d, d);
        }
    }
}
