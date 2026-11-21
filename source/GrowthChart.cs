using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// Small line chart of six stats from level 1 to 99. Optional "original" curves (from the
    /// clean ROM) are drawn dashed and faint, and a marker shows the preview level.
    /// </summary>
    class GrowthChart : Panel
    {
        public static readonly string[] Names = { "HP", "STR", "AGL", "INT", "GUT", "MGR" };
        public static readonly Color[] Colors = {
            Color.FromArgb(205, 45, 45),   // HP   red
            Color.FromArgb(225, 125, 20),  // STR  orange
            Color.FromArgb(40, 150, 60),   // AGL  green
            Color.FromArgb(40, 90, 210),   // INT  blue
            Color.FromArgb(140, 60, 170),  // GUT  purple
            Color.FromArgb(20, 150, 150)   // MGR  teal
        };
        public const int MaxLevel = 99;

        /// <summary>Legend names for the six lines (capsule stats by default).</summary>
        public string[] SeriesNames = Names;

        private int[][] current;   // [stat][level-1]
        private int[][] original;  // may be null
        private int markerLevel = 1;
        private int shadeFrom = 0, shadeTo = 0;   // level range to shade (0 = none)

        /// <summary>Shades a level range (e.g. the growth band being edited); 0, 0 clears it.</summary>
        public void SetShade(int fromLevel, int toLevel)
        {
            shadeFrom = fromLevel; shadeTo = toLevel;
            Invalidate();
        }

        public GrowthChart()
        {
            this.DoubleBuffered = true;
            this.ResizeRedraw = true;
            this.BackColor = Color.White;
            this.BorderStyle = BorderStyle.FixedSingle;
        }

        public void SetData(int[][] cur, int[][] orig, int level)
        {
            current = cur;
            original = orig;
            markerLevel = level;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Font small = new Font(this.Font.FontFamily, 7.5f);

            int left = 34, right = this.ClientSize.Width - 8, top = 22, bottom = this.ClientSize.Height - 18;
            if (right - left < 20 || bottom - top < 20) { small.Dispose(); return; }

            // Legend
            int lx = left;
            for (int s = 0; s < 6; s++)
            {
                using (SolidBrush b = new SolidBrush(Colors[s])) g.FillRectangle(b, lx, 7, 10, 3);
                g.DrawString(SeriesNames[s], small, Brushes.DimGray, lx + 12, 2);
                lx += 42;
            }
            if (original != null) g.DrawString("dashed = clean ROM", small, Brushes.Gray, lx + 4, 2);

            if (current == null) { small.Dispose(); return; }

            int max = 10;
            for (int s = 0; s < 6; s++)
                for (int i = 0; i < MaxLevel; i++)
                {
                    if (current[s][i] > max) max = current[s][i];
                    if (original != null && original[s][i] > max) max = original[s][i];
                }
            int step = max <= 100 ? 25 : max <= 300 ? 50 : max <= 600 ? 100 : 250;
            max = ((max + step - 1) / step) * step;

            // Shaded band
            if (shadeTo > shadeFrom)
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(255, 245, 205)))
                {
                    int x1 = X(Math.Max(1, shadeFrom), left, right), x2 = X(Math.Min(MaxLevel, shadeTo), left, right);
                    g.FillRectangle(sb, x1, top, Math.Max(2, x2 - x1), bottom - top);
                }

            // Grid and axes
            using (Pen grid = new Pen(Color.FromArgb(232, 232, 232)))
            {
                for (int v = 0; v <= max; v += step)
                {
                    int y = Y(v, max, top, bottom);
                    g.DrawLine(grid, left, y, right, y);
                    g.DrawString(v.ToString(), small, Brushes.Gray, 2, y - 6);
                }
                int[] marks = { 1, 25, 50, 75, 99 };
                foreach (int lv in marks)
                {
                    int x = X(lv, left, right);
                    g.DrawLine(grid, x, top, x, bottom);
                    g.DrawString(lv.ToString(), small, Brushes.Gray, x - 6, bottom + 2);
                }
            }
            g.DrawRectangle(Pens.Silver, left, top, right - left, bottom - top);

            // Original curves (dashed, faint), then current curves
            if (original != null)
                for (int s = 0; s < 6; s++)
                    using (Pen p = new Pen(Color.FromArgb(110, Colors[s]), 1.2f))
                    {
                        p.DashStyle = DashStyle.Dash;
                        DrawCurve(g, p, original[s], max, left, right, top, bottom);
                    }
            for (int s = 0; s < 6; s++)
                using (Pen p = new Pen(Colors[s], 1.8f))
                    DrawCurve(g, p, current[s], max, left, right, top, bottom);

            // Preview level marker
            int mx = X(markerLevel, left, right);
            using (Pen mp = new Pen(Color.FromArgb(150, 0, 0, 0), 1f))
            {
                mp.DashStyle = DashStyle.Dot;
                g.DrawLine(mp, mx, top, mx, bottom);
            }
            g.DrawString("Lv " + markerLevel, small, Brushes.Black, Math.Min(mx + 2, right - 34), top + 2);
            small.Dispose();
        }

        private static int X(int level, int left, int right)
        {
            return left + (int)((long)(level - 1) * (right - left) / (MaxLevel - 1));
        }

        private static int Y(int value, int max, int top, int bottom)
        {
            return bottom - (int)((long)value * (bottom - top) / Math.Max(1, max));
        }

        private static void DrawCurve(Graphics g, Pen p, int[] values, int max, int left, int right, int top, int bottom)
        {
            Point[] pts = new Point[MaxLevel];
            for (int i = 0; i < MaxLevel; i++) pts[i] = new Point(X(i + 1, left, right), Y(values[i], max, top, bottom));
            g.DrawLines(p, pts);
        }
    }
}
