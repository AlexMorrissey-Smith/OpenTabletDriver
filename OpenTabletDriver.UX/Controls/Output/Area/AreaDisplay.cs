using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX.Controls.Output.Area
{
    public class AreaDisplay : Drawable
    {
        /// <summary>
        /// Workaround for memory leaks on macOS.
        /// Use shared FormattedText to draw text.
        /// </summary>
        private class TextDrawer
        {
            private readonly FormattedText sharedFormattedText = new();

            public void DrawText(Graphics graphics, Font font, Brush brush, PointF location, String text)
            {
                sharedFormattedText.Text = text;
                sharedFormattedText.Font = font;
                sharedFormattedText.ForegroundBrush = brush;
                graphics.DrawText(sharedFormattedText, location);
            }
        }

        private AreaSettings? area;
        private bool lockToUsableArea;
        private string? unit, invalidForegroundError;
        protected IEnumerable<RectangleF>? areaBounds;
        private RectangleF? fullAreaBounds;
        private readonly TextDrawer textDrawer = new();

        public event EventHandler<EventArgs>? AreaChanged;
        public event EventHandler<EventArgs>? LockToUsableAreaChanged;
        public event EventHandler<EventArgs>? UnitChanged;
        public event EventHandler<EventArgs>? AreaBoundsChanged;
        public event EventHandler<EventArgs>? FullAreaBoundsChanged;
        public event EventHandler<EventArgs>? InvalidForegroundErrorChanged;

        protected virtual void OnAreaChanged() => AreaChanged?.Invoke(this, EventArgs.Empty);
        protected virtual void OnLockToUsableAreaChanged() => LockToUsableAreaChanged?.Invoke(this, EventArgs.Empty);
        protected virtual void OnUnitChanged() => UnitChanged?.Invoke(this, EventArgs.Empty);
        protected virtual void OnAreaBoundsChanged() => AreaBoundsChanged?.Invoke(this, EventArgs.Empty);
        protected virtual void OnFullAreaBoundsChanged() => FullAreaBoundsChanged?.Invoke(this, EventArgs.Empty);
        protected virtual void OnInvalidForegroundErrorChanged() => InvalidForegroundErrorChanged?.Invoke(this, EventArgs.Empty);

        public AreaSettings? Area
        {
            set
            {
                this.area = value;
                this.OnAreaChanged();
            }
            get => this.area;
        }

        public bool LockToUsableArea
        {
            set
            {
                this.lockToUsableArea = value;
                this.OnLockToUsableAreaChanged();
            }
            get => this.lockToUsableArea;
        }

        public string? Unit
        {
            set
            {
                this.unit = value;
                this.OnUnitChanged();
            }
            get => this.unit;
        }

        public virtual IEnumerable<RectangleF>? AreaBounds
        {
            set
            {
                this.areaBounds = value;
                this.OnAreaBoundsChanged();
            }
            get => this.areaBounds;
        }

        public RectangleF? FullAreaBounds
        {
            protected set
            {
                this.fullAreaBounds = value;
                this.OnFullAreaBoundsChanged();
            }
            get => this.fullAreaBounds;
        }

        public string? InvalidForegroundError
        {
            set
            {
                this.invalidForegroundError = value;
                this.OnInvalidForegroundErrorChanged();
            }
            get => this.invalidForegroundError;
        }

        public BindableBinding<AreaDisplay, AreaSettings?> AreaBinding
        {
            get
            {
                return new BindableBinding<AreaDisplay, AreaSettings?>(
                    this,
                    c => c.Area,
                    (c, v) => c.Area = v,
                    (c, h) => c.AreaChanged += h,
                    (c, h) => c.AreaChanged -= h
                );
            }
        }

        public BindableBinding<AreaDisplay, bool> LockToUsableAreaBinding
        {
            get
            {
                return new BindableBinding<AreaDisplay, bool>(
                    this,
                    c => c.LockToUsableArea,
                    (c, v) => c.LockToUsableArea = v,
                    (c, h) => c.LockToUsableAreaChanged += h,
                    (c, h) => c.LockToUsableAreaChanged -= h
                );
            }
        }

        public BindableBinding<AreaDisplay, string?> UnitBinding
        {
            get
            {
                return new BindableBinding<AreaDisplay, string?>(
                    this,
                    c => c.Unit,
                    (c, v) => c.Unit = v,
                    (c, h) => c.UnitChanged += h,
                    (c, h) => c.UnitChanged -= h
                );
            }
        }

        public BindableBinding<AreaDisplay, IEnumerable<RectangleF>?> AreaBoundsBinding
        {
            get
            {
                return new BindableBinding<AreaDisplay, IEnumerable<RectangleF>?>(
                    this,
                    c => c.AreaBounds,
                    (c, v) => c.AreaBounds = v,
                    (c, h) => c.AreaBoundsChanged += h,
                    (c, h) => c.AreaBoundsChanged -= h
                );
            }
        }

        public BindableBinding<AreaDisplay, RectangleF?> FullAreaBoundsBinding
        {
            get
            {
                return new BindableBinding<AreaDisplay, RectangleF?>(
                    this,
                    c => c.FullAreaBounds,
                    (c, v) => c.FullAreaBounds = v,
                    (c, h) => c.FullAreaBoundsChanged += h,
                    (c, h) => c.FullAreaBoundsChanged -= h
                );
            }
        }

        public BindableBinding<AreaDisplay, string?> InvalidForegroundErrorBinding
        {
            get
            {
                return new BindableBinding<AreaDisplay, string?>(
                    this,
                    c => c.InvalidForegroundError,
                    (c, v) => c.InvalidForegroundError = v,
                    (c, h) => c.InvalidForegroundErrorChanged += h,
                    (c, h) => c.InvalidForegroundErrorChanged -= h
                );
            }
        }

        private static readonly Font Font = SystemFonts.User(8);

        // Read SystemColors fresh on every paint (not cached) so these track a live light/dark switch.
        private static Brush TextBrush => new SolidBrush(SystemColors.ControlText);
        private static Color AccentColor => new Color(SystemColors.Highlight, 0.5f);
        private static Color AreaBoundsFillColor => SystemColors.ControlBackground;
        private static Color AreaBoundsBorderColor => SystemInterop.CurrentPlatform switch
        {
            PluginPlatform.Windows => new Color(64, 64, 64),
            _ => SystemColors.Control
        };

        [Flags]
        private enum Grip { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8, Move = 16 }

        private const float HandlePx = 8f;     // grab tolerance + drawn handle size (client px)
        private const float MinModelSize = 1f; // smallest allowed area in model units
        private const float SnapPx = 10f;      // subtle magnetic snap distance (client px)

        private Grip activeGrip = Grip.None;
        private PointF? mouseOffset;
        private PointF? viewModelOffset;
        private float dragStartAspect = 1f;    // Width/Height captured when a resize starts (Shift lock)

        private RectangleF ForegroundRect => Area == null ? RectangleF.Empty : RectangleF.FromCenter(
            new PointF(Area.X, Area.Y),
            new SizeF(Area.Width, Area.Height)
        );

        public float PixelScale => CalculateScale(FullAreaBounds ?? throw new InvalidOperationException($"Unable to look up pixel scale when {nameof(FullAreaBounds)} is unset"));

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Buttons != MouseButtons.Primary || Area == null)
            {
                activeGrip = Grip.None;
                return;
            }

            activeGrip = HitTest(e.Location);
            if (activeGrip == Grip.Move)
            {
                mouseOffset = e.Location;
                viewModelOffset = new PointF(Area.X, Area.Y);
            }
            else if (activeGrip != Grip.None && Area.Height != 0)
            {
                dragStartAspect = Area.Width / Area.Height;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            activeGrip = Grip.None;
            mouseOffset = null;
            viewModelOffset = null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (Area == null)
                return;

            if (activeGrip == Grip.Move)
            {
                if (mouseOffset.HasValue && viewModelOffset.HasValue)
                {
                    Area.X = viewModelOffset.Value.X + (e.Location.X - mouseOffset.Value.X) / PixelScale;
                    Area.Y = viewModelOffset.Value.Y + (e.Location.Y - mouseOffset.Value.Y) / PixelScale;
                    SnapMove();
                    OnAreaChanged();
                    Invalidate();
                }
            }
            else if (activeGrip != Grip.None)
            {
                ResizeTo(e.Location, activeGrip, e.Modifiers);
            }
            else
            {
                Cursor = CursorFor(HitTest(e.Location));
            }
        }

        // ---- Drag-resize geometry -------------------------------------------------------------

        private PointF PaintOffset()
        {
            var scale = PixelScale;
            var bounds = FullAreaBounds!.Value;
            return new PointF(
                (ClientSize.Width / 2f) - (bounds.Width / 2f * scale),
                (ClientSize.Height / 2f) - (bounds.Height / 2f * scale));
        }

        // Mouse client point -> area-local coords (centered on the area, un-rotated).
        private (float x, float y) ToLocal(PointF client)
        {
            var scale = PixelScale;
            var offset = PaintOffset();
            float dx = ((client.X - offset.X) / scale) - Area!.X;
            float dy = ((client.Y - offset.Y) / scale) - Area.Y;
            float rad = -Area.Rotation * MathF.PI / 180f;
            return (dx * MathF.Cos(rad) - dy * MathF.Sin(rad),
                    dx * MathF.Sin(rad) + dy * MathF.Cos(rad));
        }

        private Grip HitTest(PointF client)
        {
            if (Area == null || !IsValid(FullAreaBounds))
                return Grip.None;

            var scale = PixelScale;
            if (scale <= 0)
                return Grip.None;

            var (lx, ly) = ToLocal(client);
            float hw = Area.Width / 2f, hh = Area.Height / 2f;
            float t = HandlePx / scale;

            if (lx < -hw - t || lx > hw + t || ly < -hh - t || ly > hh + t)
                return Grip.None;

            var grip = Grip.None;
            if (MathF.Abs(lx + hw) <= t) grip |= Grip.Left;
            else if (MathF.Abs(lx - hw) <= t) grip |= Grip.Right;
            if (MathF.Abs(ly + hh) <= t) grip |= Grip.Top;
            else if (MathF.Abs(ly - hh) <= t) grip |= Grip.Bottom;

            return grip != Grip.None ? grip : Grip.Move;
        }

        private void ResizeTo(PointF client, Grip grip, Keys modifiers)
        {
            var scale = PixelScale;
            var (lx, ly) = ToLocal(client);
            float hw = Area!.Width / 2f, hh = Area.Height / 2f;
            float left = -hw, right = hw, top = -hh, bottom = hh;

            bool gl = grip.HasFlag(Grip.Left), gr = grip.HasFlag(Grip.Right);
            bool gt = grip.HasFlag(Grip.Top), gb = grip.HasFlag(Grip.Bottom);
            bool alt = modifiers.HasFlag(Keys.Alt);
            bool shift = modifiers.HasFlag(Keys.Shift);

            // 1. dragged edges follow the cursor
            if (gl) left = lx;
            if (gr) right = lx;
            if (gt) top = ly;
            if (gb) bottom = ly;

            // 2. subtle magnetic snap to display/area boundaries (only when unrotated)
            if (Area.Rotation == 0 && AreaBounds != null && scale > 0)
            {
                float t = SnapPx / scale;
                if (gl) left = SnapEdge(Area.X + left, VerticalEdges(), t) - Area.X;
                if (gr) right = SnapEdge(Area.X + right, VerticalEdges(), t) - Area.X;
                if (gt) top = SnapEdge(Area.Y + top, HorizontalEdges(), t) - Area.Y;
                if (gb) bottom = SnapEdge(Area.Y + bottom, HorizontalEdges(), t) - Area.Y;
            }

            // 3. Option/Alt: symmetric resize anchored at the center
            if (alt)
            {
                if (gl) right = -left;
                if (gr) left = -right;
                if (gt) bottom = -top;
                if (gb) top = -bottom;
            }

            // 4. Shift: maintain the aspect ratio captured at drag start
            if (shift && dragStartAspect > 0)
            {
                if (gl || gr)
                    ApplyVerticalExtent(ref top, ref bottom, MathF.Abs(right - left) / dragStartAspect, gt, gb, alt);
                else if (gt || gb)
                    ApplyHorizontalExtent(ref left, ref right, MathF.Abs(bottom - top) * dragStartAspect, gl, gr, alt);
            }

            // 5. enforce a minimum size without flipping
            if (right - left < MinModelSize) { if (gl) left = right - MinModelSize; else right = left + MinModelSize; }
            if (bottom - top < MinModelSize) { if (gt) top = bottom - MinModelSize; else bottom = top + MinModelSize; }

            float localCx = (left + right) / 2f, localCy = (top + bottom) / 2f;
            float rad = Area.Rotation * MathF.PI / 180f;
            Area.X += localCx * MathF.Cos(rad) - localCy * MathF.Sin(rad);
            Area.Y += localCx * MathF.Sin(rad) + localCy * MathF.Cos(rad);
            Area.Width = right - left;
            Area.Height = bottom - top;
            OnAreaChanged();
            Invalidate();
        }

        private static void ApplyVerticalExtent(ref float top, ref float bottom, float targetH, bool topActive, bool bottomActive, bool alt)
        {
            if (alt) { top = -targetH / 2f; bottom = targetH / 2f; }
            else if (topActive) top = bottom - targetH;
            else if (bottomActive) bottom = top + targetH;
            else { float c = (top + bottom) / 2f; top = c - targetH / 2f; bottom = c + targetH / 2f; }
        }

        private static void ApplyHorizontalExtent(ref float left, ref float right, float targetW, bool leftActive, bool rightActive, bool alt)
        {
            if (alt) { left = -targetW / 2f; right = targetW / 2f; }
            else if (leftActive) left = right - targetW;
            else if (rightActive) right = left + targetW;
            else { float c = (left + right) / 2f; left = c - targetW / 2f; right = c + targetW / 2f; }
        }

        // Subtle magnetic snap of the whole area to display/area boundaries while moving.
        private void SnapMove()
        {
            if (Area == null || Area.Rotation != 0 || AreaBounds == null)
                return;
            var scale = PixelScale;
            if (scale <= 0)
                return;

            float t = SnapPx / scale;
            float hw = Area.Width / 2f, hh = Area.Height / 2f;

            float dxLeft = SnapEdge(Area.X - hw, VerticalEdges(), t) - (Area.X - hw);
            float dxRight = SnapEdge(Area.X + hw, VerticalEdges(), t) - (Area.X + hw);
            Area.X += PickSnap(dxLeft, dxRight);

            float dyTop = SnapEdge(Area.Y - hh, HorizontalEdges(), t) - (Area.Y - hh);
            float dyBottom = SnapEdge(Area.Y + hh, HorizontalEdges(), t) - (Area.Y + hh);
            Area.Y += PickSnap(dyTop, dyBottom);
        }

        private static float PickSnap(float a, float b)
        {
            if (a != 0 && b != 0) return MathF.Abs(a) <= MathF.Abs(b) ? a : b;
            return a != 0 ? a : b;
        }

        private IEnumerable<float> VerticalEdges()
        {
            foreach (var r in AreaBounds!) { yield return r.Left; yield return r.Right; }
        }

        private IEnumerable<float> HorizontalEdges()
        {
            foreach (var r in AreaBounds!) { yield return r.Top; yield return r.Bottom; }
        }

        private static float SnapEdge(float value, IEnumerable<float> edges, float threshold)
        {
            float best = value, bestDist = threshold;
            foreach (var e in edges)
            {
                float d = MathF.Abs(e - value);
                if (d < bestDist) { bestDist = d; best = e; }
            }
            return best;
        }

        private static Cursor CursorFor(Grip grip) => grip switch
        {
            Grip.Move => Cursors.Move,
            Grip.Left or Grip.Right => Cursors.VerticalSplit,
            Grip.Top or Grip.Bottom => Cursors.HorizontalSplit,
            Grip.None => Cursors.Default,
            _ => Cursors.Crosshair // corners
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;

            switch (IsValid(ForegroundRect), IsValid(FullAreaBounds))
            {
                case (true, true):
                {
                    using (graphics.SaveTransformState())
                    {
                        var fullAreaBoundsVal = FullAreaBounds!.Value;
                        float scale = CalculateScale(fullAreaBoundsVal);

                        var clientCenter = new PointF(this.ClientSize.Width, this.ClientSize.Height) / 2;
                        var backgroundCenter = new PointF(fullAreaBoundsVal.Width, fullAreaBoundsVal.Height) / 2 * scale;
                        var offset = clientCenter - backgroundCenter;

                        graphics.TranslateTransform(offset);

                        DrawBackground(graphics, scale);
                        DrawForeground(graphics, scale);
                    }
                    break;
                }
                case (false, _):
                {
                    if (InvalidForegroundError != null)
                        DrawText(graphics, InvalidForegroundError);
                    break;
                }
            }
        }

        private void DrawBackground(Graphics graphics, float scale)
        {
            Debug.Assert(FullAreaBounds.HasValue);
            Debug.Assert(AreaBounds != null);

            using (graphics.SaveTransformState())
            {
                graphics.TranslateTransform(-FullAreaBounds.Value.TopLeft * scale);
                foreach (var rect in AreaBounds)
                {
                    var scaledRect = rect * scale;
                    graphics.FillRectangle(AreaBoundsFillColor, scaledRect);
                    graphics.DrawRectangle(AreaBoundsBorderColor, scaledRect);
                }
            }
        }

        private void DrawForeground(Graphics graphics, float scale)
        {
            if (Area == null) return;

            using (graphics.SaveTransformState())
            {
                var area = ForegroundRect * scale;

                graphics.TranslateTransform(area.Center);
                graphics.RotateTransform(Area.Rotation);
                graphics.TranslateTransform(-area.Center);

                graphics.FillRectangle(AccentColor, area);
                graphics.DrawRectangle(SystemColors.ControlText, area);

                var originEllipse = new RectangleF(0, 0, 1, 1);
                originEllipse.Offset(area.Center - (originEllipse.Size / 2));
                graphics.DrawEllipse(SystemColors.ControlText, originEllipse);

                DrawHandles(graphics, area);
                DrawRatioText(graphics, area, Area);
                DrawWidthText(graphics, area, Area);
                DrawHeightText(graphics, area, Area);
            }
        }

        private static void DrawHandles(Graphics graphics, RectangleF area)
        {
            const float hs = HandlePx;
            var points = new[]
            {
                area.TopLeft, area.TopRight, area.BottomLeft, area.BottomRight,
                area.MiddleTop, area.MiddleBottom, area.MiddleLeft, area.MiddleRight
            };
            foreach (var p in points)
            {
                var handle = new RectangleF(p.X - hs / 2, p.Y - hs / 2, hs, hs);
                graphics.FillRectangle(SystemColors.ControlText, handle);
            }
        }

        private void DrawRatioText(Graphics graphics, RectangleF area, AreaSettings areaSettings)
        {
            string ratio = Math.Round(areaSettings.Width / areaSettings.Height, 4).ToString();
            SizeF ratioMeasure = graphics.MeasureString(Font, ratio);
            var offsetY = area.Center.Y + (ratioMeasure.Height / 2);
            if (offsetY + ratioMeasure.Height > area.Y + area.Height)
                offsetY = area.Y + area.Height;

            var ratioPos = new PointF(
                area.Center.X - (ratioMeasure.Width / 2),
                offsetY
            );
            textDrawer.DrawText(graphics, Font, TextBrush, ratioPos, ratio);
        }

        private void DrawWidthText(Graphics graphics, RectangleF area, AreaSettings areaSettings)
        {
            var minDist = area.Center.Y - 40;
            string widthText = $"{MathF.Round(areaSettings.Width, 3)}{Unit}";
            var widthTextSize = graphics.MeasureString(Font, widthText);
            var widthTextPos = new PointF(
                area.MiddleTop.X - (widthTextSize.Width / 2),
                Math.Min(area.MiddleTop.Y, minDist)
            );
            textDrawer.DrawText(graphics, Font, TextBrush, widthTextPos, widthText);
        }

        private void DrawHeightText(Graphics graphics, RectangleF area, AreaSettings areaSettings)
        {
            using (graphics.SaveTransformState())
            {
                var minDist = area.Center.X - 40;
                string heightText = $"{MathF.Round(areaSettings.Height, 3)}{Unit}";
                var heightSize = graphics.MeasureString(Font, heightText) / 2;
                var heightPos = new PointF(
                    -area.MiddleLeft.Y - heightSize.Width,
                    Math.Min(area.MiddleLeft.X, minDist)
                );
                graphics.RotateTransform(-90);
                textDrawer.DrawText(graphics, Font, TextBrush, heightPos, heightText);
            }
        }

        private void DrawText(Graphics graphics, string errorText)
        {
            var errSize = graphics.MeasureString(Font, errorText);
            var errorOffset = new PointF(errSize.Width, errSize.Height) / 2;
            var clientOffset = new PointF(this.ClientSize.Width, this.ClientSize.Height) / 2;
            var offset = clientOffset - errorOffset;

            textDrawer.DrawText(graphics, Font, TextBrush, offset, errorText);
        }

        private float CalculateScale(RectangleF rect)
        {
            float scaleX = (this.ClientSize.Width - 2) / rect.Width;
            float scaleY = (this.ClientSize.Height - 2) / rect.Height;
            return scaleX > scaleY ? scaleY : scaleX;
        }

        private static bool IsValid([NotNullWhen(true)] RectangleF? rect) => rect is { Width: > 0, Height: > 0 };
    }
}
