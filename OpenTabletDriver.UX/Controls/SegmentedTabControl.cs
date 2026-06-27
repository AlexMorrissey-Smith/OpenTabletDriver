using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;

namespace OpenTabletDriver.UX.Controls
{
    public sealed class SegmentedTabControl : Panel
    {
        private readonly List<SegmentedTabPage> pages = [];
        private readonly Panel contentPanel = new();
        private readonly TabStrip tabStrip;
        private SegmentedTabPage? selectedPage;

        public SegmentedTabControl()
        {
            tabStrip = new TabStrip(this);

            Content = new StackLayout
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Spacing = 0,
                Items =
                {
                    new StackLayoutItem(tabStrip),
                    new StackLayoutItem(contentPanel, true)
                }
            };
        }

        public SegmentedTabPage? SelectedPage
        {
            set
            {
                if (selectedPage == value || value is { Visible: false })
                    return;

                selectedPage = value;
                contentPanel.Content = selectedPage?.Content;
                tabStrip.Invalidate();
            }
            get => selectedPage;
        }

        public int SelectedIndex
        {
            set => SelectedPage = value >= 0 && value < VisiblePages.Count ? VisiblePages[value] : null;
            get => SelectedPage != null ? VisiblePages.IndexOf(SelectedPage) : -1;
        }

        public IReadOnlyList<SegmentedTabPage> Pages => pages;
        private List<SegmentedTabPage> VisiblePages => pages.Where(p => p.Visible).ToList();

        public SegmentedTabPage AddPage(string text, Control content, bool visible = true)
        {
            var page = new SegmentedTabPage(text, content, visible);
            pages.Add(page);
            RefreshSelection();
            return page;
        }

        public void InsertPage(int index, SegmentedTabPage page)
        {
            pages.Insert(Math.Clamp(index, 0, pages.Count), page);
            RefreshSelection();
        }

        public int IndexOf(SegmentedTabPage page) => pages.IndexOf(page);

        public void SetPageVisible(SegmentedTabPage page, bool visible)
        {
            page.Visible = visible;
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (selectedPage is not { Visible: true })
                selectedPage = VisiblePages.FirstOrDefault();

            contentPanel.Content = selectedPage?.Content;
            tabStrip.Invalidate();
        }

        private sealed class TabStrip : Drawable
        {
            private const float StripHeight = 34;
            private const float TabHeight = 24;
            private const float TabRadius = 5;
            private const float HorizontalPadding = 12;
            private const float TabGap = 6;

            private static readonly Font Font = SystemFonts.User(9);
            private static readonly Font SelectedFont = SystemFonts.Bold(9);

            private readonly SegmentedTabControl owner;
            private readonly List<(RectangleF Rect, SegmentedTabPage Page)> hitRects = [];

            public TabStrip(SegmentedTabControl owner)
            {
                this.owner = owner;
                Height = (int)StripHeight;
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);

                if (e.Buttons != MouseButtons.Primary)
                    return;

                var hit = hitRects.FirstOrDefault(h => h.Rect.Contains(e.Location));
                if (hit.Page != null)
                    owner.SelectedPage = hit.Page;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);

                var graphics = e.Graphics;
                hitRects.Clear();

                var x = 6f;
                var y = (StripHeight - TabHeight) / 2;

                foreach (var page in owner.VisiblePages)
                {
                    var selected = page == owner.SelectedPage;
                    var font = selected ? SelectedFont : Font;
                    var textSize = graphics.MeasureString(font, page.Text);
                    var rect = new RectangleF(x, y, textSize.Width + (HorizontalPadding * 2), TabHeight);

                    if (selected)
                    {
                        using var path = GraphicsPath.GetRoundRect(rect, TabRadius);
                        graphics.FillPath(SystemColors.Control, path);
                        graphics.DrawPath(new Pen(new Color(SystemColors.ControlText, 0.25f)), path);
                    }

                    var textPoint = new PointF(
                        rect.X + HorizontalPadding,
                        rect.Y + ((rect.Height - textSize.Height) / 2)
                    );
                    graphics.DrawText(font, SystemColors.ControlText, textPoint, page.Text);

                    hitRects.Add((rect, page));
                    x = rect.Right + TabGap;
                }

                graphics.DrawLine(new Pen(new Color(SystemColors.ControlText, 0.15f)), 0, StripHeight - 1, ClientSize.Width, StripHeight - 1);
            }
        }
    }

    public sealed class SegmentedTabPage
    {
        public SegmentedTabPage(string text, Control content, bool visible)
        {
            Text = text;
            Content = content;
            Visible = visible;
        }

        public string Text { get; set; }
        public Control Content { get; }
        public bool Visible { get; set; }
    }
}
