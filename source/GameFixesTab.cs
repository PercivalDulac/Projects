using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Game Fixes" tab: one tick box per bug fix (see GameFixes). Ticking writes the fixed bytes,
    /// unticking puts the original game's bytes back. A fix whose bytes have been changed by
    /// something else is shown but left alone.
    /// </summary>
    class GameFixesTab : TabPage
    {
        private Rom rom;
        private GameFixes fixes;
        private bool loading = false;

        private Panel panel;
        private Button buttonAll;
        private readonly List<CheckBox> boxes = new List<CheckBox>();
        private readonly List<Label> states = new List<Label>();

        /// <summary>Raised after a fix is switched, so other tabs can re-read what changed.</summary>
        public event EventHandler FixesChanged;

        private const int PanelWidth = 958;   // the tab page is 982 x 515: keep the panel (and its scroll bar) inside it
        private static readonly Font GroupFont = new Font(SystemFonts.DefaultFont, FontStyle.Bold);

        public GameFixesTab()
        {
            this.Text = "Game Fixes";
            this.UseVisualStyleBackColor = true;

            Label intro = UiHelpers.MakeLabel(
                "Fixes for bugs in the original game. Tick one to apply it; untick it to put the original back.", 12, 10);
            Controls.Add(intro);
            Label credit = UiHelpers.MakeNote(
                "From Relnqshd's Lufia II Fixxxer Deluxe (1.0 beta) and his Artea name patch, and those who helped him. " +
                "He never finished testing Fixxxer, so try each fix in game.", 12, 30);
            Controls.Add(credit);
            buttonAll = new Button();
            buttonAll.Text = "Turn on all fixes";
            buttonAll.Location = new Point(12, 52);
            buttonAll.Size = new Size(120, 24);
            buttonAll.Click += new EventHandler(buttonAll_Click);
            Controls.Add(buttonAll);
            Controls.Add(UiHelpers.MakeNote("The Ancient Cave floor 10+ equipment fix is on the Cave Loot tab.", 142, 57));

            panel = new Panel();
            panel.Location = new Point(12, 84);
            panel.Size = new Size(PanelWidth, 418);
            panel.AutoScroll = true;
            panel.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(panel);
            UiHelpers.Anchor(this, panel, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right);
            this.Enabled = false;
        }

        // ---------------- loading ----------------

        public void LoadRom(Rom romData)
        {
            rom = romData;
            fixes = Make(rom.RawData, rom.HasHeader);
            Build();
            this.Enabled = true;
            RefreshView();
        }

        public void Disable()
        {
            rom = null; fixes = null;
            this.Enabled = false;
        }

        /// <summary>Nothing to store: every tick is written to the ROM data at once.</summary>
        public void CommitCurrent() { }

        private static GameFixes Make(byte[] d, bool hdr)
        {
            return GameFixes.For(d, hdr);
        }

        private void Build()
        {
            UiHelpers.ClearAndDispose(panel);
            boxes.Clear(); states.Clear();
            int y = 8;
            string group = null;
            for (int i = 0; i < fixes.Fixes.Count; i++)
            {
                GameFixes.Fix f = fixes.Fixes[i];
                if (f.Group != group)
                {
                    group = f.Group;
                    Label g = UiHelpers.MakeLabel(group, 8, y);
                    g.Font = GroupFont;
                    panel.Controls.Add(g);
                    y += 22;
                }
                CheckBox c = new CheckBox();
                c.Text = f.Name;
                c.Location = new Point(16, y);
                c.AutoSize = true;
                c.Tag = i;
                c.CheckedChanged += new EventHandler(box_CheckedChanged);
                panel.Controls.Add(c);
                boxes.Add(c);
                y += 22;

                Label note = UiHelpers.MakeNote(f.Detail, 34, y);
                note.MaximumSize = new Size(PanelWidth - 70, 0);
                panel.Controls.Add(note);
                y += note.GetPreferredSize(new Size(PanelWidth - 70, 0)).Height + 2;

                Label st = UiHelpers.MakeLabel("", 34, y);
                st.ForeColor = Color.DarkRed;
                st.MaximumSize = new Size(PanelWidth - 70, 0);
                st.Visible = false;
                panel.Controls.Add(st);
                states.Add(st);
                y += 22;
            }

            Label notHead = UiHelpers.MakeLabel("Not included", 8, y);
            notHead.Font = GroupFont;
            panel.Controls.Add(notHead);
            y += 22;
            Label notText = UiHelpers.MakeNote(
                "Fixxxer's other enemy fixes (Nosferato's Contact healing him, Copper Dragon's and Ninja's resistances, " +
                "Erim's spells) make those enemies' scripts longer. The enemy data has no spare room, and Fixxxer " +
                "moves all of it into expanded space to make them fit, which this editor doesn't do yet.\n" +
                "Its map, graphics and text changes (map names such as Gratze, the Submarine Shrine and Ancient Cave " +
                "final floor maps, the Shrine of Vengeance B2 background, the bunny girls, spelling) aren't included either.", 16, y);
            notText.MaximumSize = new Size(PanelWidth - 50, 0);
            panel.Controls.Add(notText);
            y += notText.GetPreferredSize(new Size(PanelWidth - 50, 0)).Height;

            // An empty label below the last text, so scrolling to the bottom leaves a margin.
            Label spacer = new Label();
            spacer.Location = new Point(0, y + 12);
            spacer.Size = new Size(1, 1);
            panel.Controls.Add(spacer);
        }

        // ---------------- showing ----------------

        /// <summary>Re-reads every fix's state (other tabs may have changed the same bytes).</summary>
        public void RefreshView()
        {
            if (rom == null) return;
            fixes = Make(rom.RawData, rom.HasHeader);
            GameFixes clean = Reference.Available ? GameFixes.For(Reference.Data, Reference.HasHeader) : null;
            bool was = loading;
            loading = true;
            bool anyOff = false;
            for (int i = 0; i < fixes.Fixes.Count && i < boxes.Count; i++)
            {
                GameFixes.Fix f = fixes.Fixes[i];
                int s = fixes.State(f);
                boxes[i].Checked = s == GameFixes.STATE_ON;
                boxes[i].Enabled = s != GameFixes.STATE_OTHER;
                if (s == GameFixes.STATE_OFF && !f.BalanceChange) anyOff = true;

                string why = null;
                if (f.Problem != null) why = "Not available for this ROM: " + f.Problem;
                else if (s == GameFixes.STATE_OTHER)
                    why = "Left alone: changed elsewhere (another tab or patch). Set it back to the original game's values to use this fix.";
                states[i].Text = why ?? "";
                states[i].Visible = why != null;

                object orig = null;
                if (clean != null && i < clean.Fixes.Count)
                {
                    int cs = clean.State(clean.Fixes[i]);
                    if (cs != GameFixes.STATE_OTHER) orig = cs == GameFixes.STATE_ON;
                }
                ChangeHighlighter.Set(boxes[i], s == GameFixes.STATE_OTHER ? null : orig);
            }
            buttonAll.Enabled = anyOff;
            loading = was;
        }

        // ---------------- editing ----------------

        private void box_CheckedChanged(object sender, EventArgs e)
        {
            if (loading || fixes == null) return;
            CheckBox c = (CheckBox)sender;
            GameFixes.Fix f = fixes.Fixes[(int)c.Tag];
            fixes.Set(f, c.Checked);
            RefreshView();
            if (FixesChanged != null) FixesChanged(this, EventArgs.Empty);
        }

        private void buttonAll_Click(object sender, EventArgs e)
        {
            if (fixes == null) return;
            bool any = false;
            foreach (GameFixes.Fix f in fixes.Fixes)
                if (!f.BalanceChange && fixes.State(f) == GameFixes.STATE_OFF) { fixes.Set(f, true); any = true; }
            RefreshView();
            if (any && FixesChanged != null) FixesChanged(this, EventArgs.Empty);
        }
    }
}
