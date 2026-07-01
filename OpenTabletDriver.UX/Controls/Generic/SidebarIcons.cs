using System;
using Eto.Drawing;

namespace OpenTabletDriver.UX.Controls.Generic
{
    // Vector sidebar glyphs drawn procedurally straight into the sidebar's Graphics, tinted per
    // row state (neutral when idle, white on the accent selection pill). Drawing on demand means
    // no bundled SVG/PNG assets, no rasterization, crisp at any DPI, and free theme adaptation -
    // the caller just passes the colour it wants.
    internal static class SidebarIcons
    {
        // All glyph paths are authored in an 18x18 design box; Draw() scales that into `rect`.
        private const float DesignSize = 18f;

        public static void Draw(Graphics g, string name, RectangleF rect, Color color)
        {
            using var _ = g.SaveTransformState();
            g.AntiAlias = true;
            g.TranslateTransform(rect.X, rect.Y);
            g.ScaleTransform(rect.Width / DesignSize, rect.Height / DesignSize);

            using var pen = new Pen(color, 1.4f);

            switch (name)
            {
                case "Tablet":
                    DrawRoundRectStroke(g, pen, 2.5f, 3.5f, 13, 11, 2f);
                    g.DrawLine(pen, 6.5f, 15.2f, 11.5f, 15.2f);
                    break;

                case "Pen":
                    g.DrawLine(pen, 3.2f, 14.8f, 12f, 6f);
                    using (var tip = new GraphicsPath())
                    {
                        tip.AddLines(new[] { new PointF(12f, 6f), new PointF(15.2f, 2.8f), new PointF(15.8f, 4.6f), new PointF(12.6f, 8f) });
                        tip.CloseFigure();
                        g.FillPath(color, tip);
                    }
                    g.DrawLine(pen, 3.2f, 14.8f, 4.6f, 15.4f);
                    break;

                case "Buttons":
                    foreach (var (x, y) in new[] { (3f, 3f), (10f, 3f), (3f, 10f), (10f, 10f) })
                        FillRoundRect(g, color, x, y, 5f, 5f, 1.2f);
                    break;

                case "Mouse":
                    DrawRoundRectStroke(g, pen, 4.5f, 2f, 9, 14, 4.5f);
                    g.DrawLine(pen, 9f, 2.4f, 9f, 7.5f);
                    break;

                case "Wheel":
                    g.DrawEllipse(pen, 3f, 3f, 12f, 12f);
                    g.FillEllipse(color, 8f, 8f, 2f, 2f);
                    g.DrawLine(pen, 9f, 3.2f, 9f, 5.4f);
                    break;

                case "Tools":
                    g.DrawLine(pen, 3.5f, 14.5f, 10.5f, 7.5f);
                    using (var head = new GraphicsPath())
                    {
                        head.AddEllipse(9f, 2f, 6.2f, 6.2f);
                        g.DrawPath(pen, head);
                    }
                    FillRoundRect(g, color, 2.2f, 13.2f, 3.2f, 3.2f, 0.8f);
                    break;

                case "Filters":
                    using (var funnel = new GraphicsPath())
                    {
                        funnel.AddLines(new[] {
                            new PointF(2.5f, 3f), new PointF(15.5f, 3f), new PointF(10f, 9.5f),
                            new PointF(10f, 15f), new PointF(8f, 13.5f), new PointF(8f, 9.5f) });
                        funnel.CloseFigure();
                        g.FillPath(color, funnel);
                    }
                    break;

                case "Info":
                    g.DrawEllipse(pen, 2.6f, 2.6f, 12.8f, 12.8f);
                    g.FillEllipse(color, 8.2f, 5f, 1.6f, 1.6f);
                    g.DrawLine(pen, 9f, 8.4f, 9f, 12.8f);
                    break;

                case "Console":
                    DrawRoundRectStroke(g, pen, 2f, 3f, 14, 12, 2.5f);
                    using (var chevron = new GraphicsPath())
                    {
                        chevron.AddLines(new[] { new PointF(5f, 7f), new PointF(8f, 9.4f), new PointF(5f, 11.8f) });
                        g.DrawPath(pen, chevron);
                    }
                    g.DrawLine(pen, 9.5f, 11.8f, 13f, 11.8f);
                    break;
            }
        }

        private static void DrawRoundRectStroke(Graphics g, Pen pen, float x, float y, float w, float h, float radius)
        {
            using var path = GraphicsPath.GetRoundRect(new RectangleF(x, y, w, h), radius);
            g.DrawPath(pen, path);
        }

        private static void FillRoundRect(Graphics g, Color color, float x, float y, float w, float h, float radius)
        {
            using var path = GraphicsPath.GetRoundRect(new RectangleF(x, y, w, h), radius);
            g.FillPath(color, path);
        }
    }
}
