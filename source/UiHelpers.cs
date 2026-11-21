using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace com.lufiaIIEditor.UI
{
    static class UiHelpers
    {
        // ---- stretching with the window ----
        // Tabs register the controls that should stretch or move when the window grows
        // (Anchor below). Form1 tracks how much the window has grown since it opened and calls
        // Relayout for the tab on screen; each control then gets its design bounds plus that
        // growth (or its parent's growth, for controls inside a group). Done by hand instead of
        // the Anchor property so it's right even for tabs that weren't laid out yet.

        private class Stretch
        {
            public Control Control;
            public AnchorStyles Anchor;
            public Rectangle Design;
            public bool HaveDesign;
            public Size Growth;
            public double MoveX = -1, GrowX = -1;   // fractions of the width growth (for side-by-side panels)
        }

        private static Dictionary<Control, List<Stretch>> stretches = new Dictionary<Control, List<Stretch>>();

        /// <summary>How much the window's client area has grown since it opened.</summary>
        public static Size WindowGrowth = Size.Empty;

        public static T Anchor<T>(Control owner, T control, AnchorStyles anchor) where T : Control
        {
            List<Stretch> list;
            if (!stretches.TryGetValue(owner, out list)) { list = new List<Stretch>(); stretches[owner] = list; }
            Stretch st = new Stretch();
            st.Control = control; st.Anchor = anchor;
            list.Add(st);
            return control;
        }

        /// <summary>
        /// Like Anchor (top and bottom), but horizontally the control moves by moveX and widens by
        /// growX of the extra width - e.g. two panels side by side each take half (0, 0.5 and 0.5, 0.5).
        /// </summary>
        public static T AnchorShare<T>(Control owner, T control, double moveX, double growX) where T : Control
        {
            Anchor(owner, control, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left);
            Stretch st = stretches[owner][stretches[owner].Count - 1];
            st.MoveX = moveX; st.GrowX = growX;
            return control;
        }

        public static void Relayout(Control owner)
        {
            List<Stretch> list;
            if (owner == null || !stretches.TryGetValue(owner, out list)) return;
            owner.SuspendLayout();
            foreach (Stretch st in list)
            {
                if (!st.HaveDesign) { st.Design = st.Control.Bounds; st.HaveDesign = true; }
                // Growth available from the parent: the whole window growth for controls placed
                // directly on the tab, otherwise whatever the parent group grew by.
                Size g = Size.Empty;
                if (st.Control.Parent == owner) g = WindowGrowth;
                else
                    foreach (Stretch p in list)
                        if (p.Control == st.Control.Parent) { g = p.Growth; break; }
                bool l = (st.Anchor & AnchorStyles.Left) != 0, r = (st.Anchor & AnchorStyles.Right) != 0;
                bool t = (st.Anchor & AnchorStyles.Top) != 0, b = (st.Anchor & AnchorStyles.Bottom) != 0;
                Rectangle d = st.Design;
                int x = d.X, y = d.Y, w = d.Width, h = d.Height, gw = 0, gh = 0;
                if (st.GrowX >= 0) { x += (int)(g.Width * st.MoveX); gw = (int)(g.Width * st.GrowX); w += gw; }
                else if (l && r) { w += g.Width; gw = g.Width; } else if (r) x += g.Width;
                if (t && b) { h += g.Height; gh = g.Height; } else if (b) y += g.Height;
                st.Growth = new Size(gw, gh);
                st.Control.Bounds = new Rectangle(x, y, Math.Max(20, w), Math.Max(20, h));
            }
            owner.ResumeLayout();
        }

        public static Label MakeLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            return l;
        }

        public static Label MakeNote(string text, int x, int y)
        {
            Label l = MakeLabel(text, x, y);
            l.ForeColor = SystemColors.GrayText;
            return l;
        }

        public static NumericUpDown MakeNumeric(int x, int y, int min, int max)
        {
            NumericUpDown n = new NumericUpDown();
            n.Location = new Point(x, y);
            n.Size = new Size(70, 20);
            n.Minimum = min;
            n.Maximum = max;
            return n;
        }

        public static ComboBox MakeDropDown(int x, int y, int width)
        {
            ComboBox c = new ComboBox();
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            c.Location = new Point(x, y);
            c.Size = new Size(width, 21);
            c.MaxDropDownItems = 25;
            return c;
        }

        /// <summary>Text box that physically cannot hold more than maxLetters characters.</summary>
        public static TextBox MakeNameBox(int x, int y, int maxLetters)
        {
            TextBox t = new TextBox();
            t.Location = new Point(x, y);
            t.Size = new Size(160, 20);
            t.MaxLength = maxLetters;
            return t;
        }

        public static GroupBox MakeGroup(string text, int x, int y, int w, int h)
        {
            GroupBox g = new GroupBox();
            g.Text = text;
            g.Location = new Point(x, y);
            g.Size = new Size(w, h);
            return g;
        }

        public static void SetValueClamped(NumericUpDown n, int v)
        {
            if (v < n.Minimum) v = (int)n.Minimum;
            if (v > n.Maximum) v = (int)n.Maximum;
            n.Value = v;
        }

        /// <summary>Removes and disposes all child controls (so nothing keeps references to them).</summary>
        public static void ClearAndDispose(Control parent)
        {
            List<Control> old = new List<Control>();
            foreach (Control c in parent.Controls) old.Add(c);
            parent.Controls.Clear();
            foreach (Control c in old) c.Dispose();
        }

        /// <summary>Replaces one combo entry without losing the current selection.</summary>
        public static void RenameItem(ComboBox c, int index, string text)
        {
            if (index < 0 || index >= c.Items.Count) return;
            if ((string)c.Items[index] == text) return;
            int selected = c.SelectedIndex;
            c.Items[index] = text;
            if (c.SelectedIndex != selected) c.SelectedIndex = selected;
        }
    }
}
