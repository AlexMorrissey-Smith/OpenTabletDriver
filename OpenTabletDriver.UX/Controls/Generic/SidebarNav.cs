using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;

namespace OpenTabletDriver.UX.Controls.Generic
{
    // Custom-drawn macOS-System-Settings-style sidebar: icon + label rows with a rounded accent
    // selection pill and a subtle hover state. Replaces the native ListBox, which renders as a
    // cramped NSTableView that can't be styled into anything modern. All colours are read fresh
    // per paint so it follows a live light/dark switch.
    public class SidebarNav : Drawable
    {
        public sealed class NavItem(string text, string iconName, object tag)
        {
            public string Text { get; } = text;
            public string IconName { get; } = iconName;
            public object Tag { get; } = tag;
        }

        private const int RowHeight = 36;
        private const int TopPadding = 8;
        private const int SideMargin = 8;
        private const int IconSize = 18;
        private const int IconLeftPad = 10;
        private const int TextGap = 10;
        private const float PillRadius = 7f;

        private readonly List<NavItem> items = [];
        private int selectedIndex = -1;
        private int hoverIndex = -1;

        private static readonly Font RowFont = SystemFonts.User(13);

        public SidebarNav()
        {
            this.BackgroundColor = Colors.Transparent;
            // Suppress the macOS focus ring (a blue rounded outline) that a Drawable draws around
            // itself when clicked; the selection pill is the only highlight we want.
            this.CanFocus = false;
        }

        public int Count => items.Count;
        public NavItem this[int index] => items[index];
        public IReadOnlyList<NavItem> Items => items;

        public bool Contains(NavItem item) => items.Contains(item);
        public int IndexOf(NavItem item) => items.IndexOf(item);

        public void Insert(int index, NavItem item)
        {
            items.Insert(index, item);
            if (index <= selectedIndex)
                selectedIndex++;
            UpdateHeight();
            Invalidate();
        }

        public void Remove(NavItem item)
        {
            int index = items.IndexOf(item);
            if (index < 0)
                return;

            items.RemoveAt(index);
            if (index == selectedIndex)
                selectedIndex = -1;
            else if (index < selectedIndex)
                selectedIndex--;

            UpdateHeight();
            Invalidate();
        }

        public NavItem? SelectedItem => selectedIndex >= 0 && selectedIndex < items.Count ? items[selectedIndex] : null;

        public int SelectedIndex
        {
            get => selectedIndex;
            set
            {
                int clamped = value >= 0 && value < items.Count ? value : -1;
                if (clamped == selectedIndex)
                    return;
                selectedIndex = clamped;
                Invalidate();
                SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler<EventArgs>? SelectedIndexChanged;

        private void UpdateHeight()
        {
            // Floor the height at what's needed to lay every row out; the splitter panel stretches
            // the drawable taller than this to fill the pane, so the sidebar surface (painted in
            // OnPaint) always covers the full height instead of leaving an empty void below.
            this.MinimumSize = new Size(0, Math.Max(TopPadding * 2 + items.Count * RowHeight, RowHeight));
        }

        private int IndexAt(PointF location)
        {
            if (location.Y < TopPadding)
                return -1;
            int index = (int)((location.Y - TopPadding) / RowHeight);
            return index >= 0 && index < items.Count ? index : -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexAt(e.Location);
            Cursor = index >= 0 ? Cursors.Pointer : Cursors.Default;
            if (index != hoverIndex)
            {
                hoverIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex != -1)
            {
                hoverIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int index = IndexAt(e.Location);
            if (index >= 0)
                SelectedIndex = index;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.AntiAlias = true;

            var window = SystemColors.WindowBackground;
            var normalText = SystemColors.ControlText;
            bool isDark = (window.R + window.G + window.B) / 3f < 0.5f;

            float width = this.ClientSize.Width;
            float height = this.ClientSize.Height;

            // Solid sidebar surface, a hair lighter than the content background (like a macOS
            // sidebar) rather than darker, covering the full height with a hairline divider
            // against the content pane. No transparency.
            var railFill = isDark ? Color.Blend(window, Colors.White, 0.04f) : Color.Blend(window, Colors.Black, 0.03f);
            var divider = isDark ? Color.Blend(window, Colors.White, 0.10f) : Color.Blend(window, Colors.Black, 0.12f);
            g.FillRectangle(railFill, new RectangleF(0, 0, width, height));
            g.FillRectangle(divider, new RectangleF(width - 1, 0, 1, height));

            // SystemColors.Highlight is a dynamic catalog NSColor on macOS; Eto.Mac can't fill a
            // GraphicsPath with it reliably (it rendered as a hollow blue outline instead of a solid
            // pill in the published build), so use concrete opaque RGB for the selection.
            var accent = isDark ? new Color(0.039f, 0.518f, 1f) : new Color(0f, 0.478f, 1f);
            var accentText = Colors.White;
            var idleIcon = Color.Blend(railFill, normalText, 0.70f);
            var hoverFill = isDark ? Color.Blend(railFill, Colors.White, 0.07f) : Color.Blend(railFill, Colors.Black, 0.05f);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                float rowTop = TopPadding + i * RowHeight;
                bool isSelected = i == selectedIndex;
                bool isHover = i == hoverIndex && !isSelected;

                var pill = new RectangleF(SideMargin, rowTop + 2, width - SideMargin * 2, RowHeight - 4);
                if (isSelected)
                    DrawingHelpers.FillRoundedRect(g, accent, pill, PillRadius);
                else if (isHover)
                    DrawingHelpers.FillRoundedRect(g, hoverFill, pill, PillRadius);

                var iconColor = isSelected ? accentText : idleIcon;
                var iconRect = new RectangleF(
                    SideMargin + IconLeftPad,
                    rowTop + (RowHeight - IconSize) / 2f,
                    IconSize, IconSize);
                SidebarIcons.Draw(g, item.IconName, iconRect, iconColor);

                var textColor = isSelected ? accentText : normalText;
                var textSize = g.MeasureString(RowFont, item.Text);
                var textPos = new PointF(
                    iconRect.Right + TextGap,
                    rowTop + (RowHeight - textSize.Height) / 2f);
                g.DrawText(RowFont, textColor, textPos, item.Text);
            }
        }
    }
}
