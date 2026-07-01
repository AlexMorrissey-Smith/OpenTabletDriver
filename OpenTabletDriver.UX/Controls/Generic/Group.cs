using System;
using Eto.Drawing;
using Eto.Forms;

namespace OpenTabletDriver.UX.Controls.Generic
{
    // A settings section. Vertical groups are "sections" and paint a rounded card behind their
    // content (macOS System Settings style); horizontal groups are inline "rows" that live inside
    // a section and stay transparent, so cards never nest inside cards. All colours are read fresh
    // per paint so the card follows a live light/dark switch.
    public class Group : Drawable
    {
        private const float CornerRadius = 10f;

        public Group()
        {
            this.BackgroundColor = Colors.Transparent;
        }

        public Group(string text, Control content, Orientation orientation = DEFAULT_ORIENTATION, bool expand = true)
            : this()
        {
            this.Text = text;
            this.Content = content;
            this.Orientation = orientation;
            this.ExpandContent = expand;
        }

        private const Orientation DEFAULT_ORIENTATION = Orientation.Vertical;

        // Only sections draw a card; rows are transparent and sit inside a section's padding, so
        // they don't need their own outer padding (just a little vertical rhythm).
        private bool IsSection => Orientation == Orientation.Vertical;

        protected virtual Padding ContentPadding => IsSection ? new Padding(16, 14, 16, 16) : new Padding(0, 5, 0, 5);

        // Solid, clearly-elevated card fill (a lighter shade of the window background in dark mode,
        // a hair darker in light mode) - no transparency, no border, definition comes from the
        // fill contrast alone, like a modern settings app.
        protected virtual Color CardBackgroundColor => Elevate(0.08f, 0.035f);

        // Muted uppercase section-header colour: pull the text partway toward the background.
        private static Color HeaderColor => Color.Blend(SystemColors.ControlText, SystemColors.WindowBackground, 0.45f);

        private static Color Elevate(float darkAmount, float lightAmount)
        {
            var window = SystemColors.WindowBackground;
            bool isDark = (window.R + window.G + window.B) / 3f < 0.5f;
            return isDark
                ? Color.Blend(window, Colors.White, darkAmount)
                : Color.Blend(window, Colors.Black, lightAmount);
        }

        public string? Text
        {
            get;
            set
            {
                field = value;
                UpdateControlLayout();
            }
        }

        public new Control? Content
        {
            get;
            set
            {
                field = value;
                UpdateControlLayout();
            }
        }

        public Orientation Orientation { set; get; } = DEFAULT_ORIENTATION;
        public bool ExpandContent { set; get; } = true;
        public HorizontalAlignment TitleHorizontalAlignment { set; get; } = HorizontalAlignment.Left;
        public VerticalAlignment TitleVerticalAlignment { set; get; } = VerticalAlignment.Center;

        protected void UpdateControlLayout()
        {
            if (!this.Loaded)
                return;

            switch (Orientation)
            {
                case Orientation.Horizontal:
                {
                    StackLayout contentLayout;
                    base.Content = contentLayout = new StackLayout
                    {
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Orientation = Orientation.Horizontal,
                        Spacing = 10,
                        Padding = ContentPadding,
                        Items =
                        {
                            new StackLayoutItem
                            {
                                VerticalAlignment = TitleVerticalAlignment,
                                Control = new Label
                                {
                                    Text = this.Text
                                }
                            },
                            new StackLayoutItem(this.Content, ExpandContent)
                        }
                    };
                    if (!ExpandContent)
                        contentLayout.Items.Insert(1, new StackLayoutItem(null, true));
                    break;
                }
                case Orientation.Vertical:
                {
                    base.Content = new StackLayout
                    {
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Spacing = 8,
                        Padding = ContentPadding,
                        Items =
                        {
                            new StackLayoutItem
                            {
                                HorizontalAlignment = TitleHorizontalAlignment,
                                Control = new Label
                                {
                                    Text = this.Text?.ToUpperInvariant(),
                                    Font = SystemFonts.Bold(8),
                                    TextColor = HeaderColor
                                }
                            },
                            new StackLayoutItem
                            {
                                Expand = true,
                                Control = this.Content
                            }
                        }
                    };
                    break;
                }
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (!IsSection)
                return;

            var bounds = new RectangleF(PointF.Empty, (SizeF)this.ClientSize);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            e.Graphics.AntiAlias = true;
            DrawingHelpers.FillRoundedRect(e.Graphics, CardBackgroundColor, bounds, CornerRadius);
        }

        protected override void OnLoadComplete(EventArgs e)
        {
            base.OnLoadComplete(e);
            UpdateControlLayout();
        }
    }
}
