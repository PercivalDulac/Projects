using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    /// <summary>
    /// "Enemy Overview" tab: every enemy in one sortable, editable table. Edits are written to the
    /// ROM data straight away (the original Enemies tab is refreshed to match through EnemyEdited).
    /// </summary>
    class EnemyGridTab : TabPage
    {
        // Column kinds
        private const int K_ID = 0, K_NAME = 1, K_BYTE = 2, K_WORD = 3, K_STAT2 = 4, K_DROPITEM = 5, K_CHANCE = 6,
            K_REALDROP = 7, K_EXPHP = 8, K_ELEM = 9, K_ATKELEM = 10, K_AI = 11, K_WHERE = 12;

        private class Col
        {
            public string Header; public int Kind; public int Offset; public int Width; public int Max; public string Tip;
            public Col(string h, int k, int o, int w, int max, string tip) { Header = h; Kind = k; Offset = o; Width = w; Max = max; Tip = tip; }
        }

        private static readonly Col[] Cols = {
            new Col("ID", K_ID, 0, 34, 0, "Enemy number (hex)"),
            new Col("Name", K_NAME, 0, 105, 0, "Up to 13 letters"),
            new Col("Lv", K_BYTE, EnemyData.OFS_LEVEL, 36, 255, "Level"),
            new Col("HP", K_WORD, EnemyData.OFS_HP, 52, 63000,
                "Max HP. Capped at 63000 like the original editor did (it warned that healing\nabove that can overflow and leave the enemy nearly dead)."),
            new Col("MP", K_WORD, EnemyData.OFS_MP, 48, 63000, "Max MP (same 63000 cap)"),
            new Col("ATP", K_WORD, EnemyData.OFS_ATP, 46, 65535, "Attack"),
            new Col("DFP", K_WORD, EnemyData.OFS_DFP, 46, 65535, "Defense"),
            new Col("AGL", K_STAT2, EnemyData.OFS_AGL, 40, 510, "Agility as used in battle. The ROM stores half and the game doubles it,\nso odd numbers round down to even."),
            new Col("INT", K_STAT2, EnemyData.OFS_INT, 40, 510, "Intelligence in battle (stored as half, doubled by the game)"),
            new Col("GUT", K_STAT2, EnemyData.OFS_GUT, 40, 510, "Guts in battle (stored as half, doubled by the game)"),
            new Col("MGR", K_STAT2, EnemyData.OFS_MGR, 40, 510, "Magic resistance in battle (stored as half, doubled by the game)"),
            new Col("EXP", K_WORD, EnemyData.OFS_EXP, 50, 65535, "Experience it gives"),
            new Col("Gold", K_WORD, EnemyData.OFS_GOLD, 48, 65535, "Gold it gives"),
            new Col("EXP/HP", K_EXPHP, 0, 50, 0, "EXP per point of HP: higher = better to grind on"),
            new Col("Drop", K_DROPITEM, 0, 130, 0,
                "Item it can drop. Enemies without a drop entry can't be given one (no room)."),
            new Col("Chance", K_CHANCE, 0, 50, 100,
                "The game first rolls 0-99 against this number, then 1 in 4 on top,\nso the real drop rate is this / 4 % (100 = 25%)."),
            new Col("Real %", K_REALDROP, 0, 48, 0, "Actual drop rate (chance / 4)"),
            new Col("Elements", K_ELEM, 0, 230, 0,
                "Weaknesses / resistances (edit them on the Enemy AI tab).\n* = only sometimes (a condition decides)."),
            new Col("Attack", K_ATKELEM, 0, 70, 0, "Element of its normal attacks (edit on the Enemy AI tab)"),
            new Col("Sprite", K_BYTE, EnemyData.OFS_SPRITE, 44, 255, "Sprite number"),
            new Col("Pattern", K_BYTE, EnemyData.OFS_PATTERN, 48, 255, "Palette / pattern number"),
            new Col("AI", K_AI, 0, 40, 0, "Open this enemy on the Enemy AI tab"),
            new Col("Where", K_WHERE, 0, 52, 0, "Where this enemy appears (Encounters tab)"),
        };

        private EnemyData enemies;
        private ItemData items;
        private DataGridView grid;
        private TextBox textFind;
        private Label labelCount;
        private bool loading;
        private List<string> dropNames = new List<string>();
        private const string NoDropSlot = "(no drop slot)";

        /// <summary>An enemy's values changed here (the original Enemies tab re-reads it).</summary>
        public event Action<int> EnemyEdited;
        /// <summary>The "AI" button was pressed for an enemy.</summary>
        public event Action<int> OpenAI;
        /// <summary>The "Where" button was pressed for an enemy.</summary>
        public event Action<int> OpenWhere;

        public EnemyGridTab()
        {
            this.Text = "Enemy Overview";
            this.UseVisualStyleBackColor = true;
            Build();
            this.Enabled = false;
        }

        private void Build()
        {
            Controls.Add(UiHelpers.MakeLabel("Find:", 12, 15));
            textFind = new TextBox();
            textFind.Location = new Point(50, 12);
            textFind.Size = new Size(160, 20);
            textFind.TextChanged += delegate { ApplyFilter(); };
            Controls.Add(textFind);
            labelCount = UiHelpers.MakeNote("", 220, 15);
            Controls.Add(labelCount);
            Controls.Add(UiHelpers.MakeNote(
                "Click a header to sort. Type in a cell to edit. Find matches names, elements and drops (e.g. \"Light\").",
                400, 15));

            grid = new DataGridView();
            grid.Location = new Point(12, 40);
            grid.Size = new Size(962, 468);
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.MultiSelect = false;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.BackgroundColor = SystemColors.Window;
            grid.RowTemplate.Height = 20;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.ShowCellToolTips = true;

            foreach (Col c in Cols)
            {
                DataGridViewColumn col;
                if (c.Kind == K_DROPITEM)
                {
                    DataGridViewComboBoxColumn cc = new DataGridViewComboBoxColumn();
                    cc.DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing;
                    cc.FlatStyle = FlatStyle.Flat;
                    cc.DropDownWidth = 200;
                    cc.MaxDropDownItems = 20;
                    col = cc;
                }
                else if (c.Kind == K_AI || c.Kind == K_WHERE)
                {
                    DataGridViewButtonColumn bc = new DataGridViewButtonColumn();
                    bc.Text = c.Kind == K_AI ? "AI" : "Where";
                    bc.UseColumnTextForButtonValue = true;
                    col = bc;
                }
                else
                {
                    DataGridViewTextBoxColumn tc = new DataGridViewTextBoxColumn();
                    if (c.Kind == K_NAME) tc.MaxInputLength = EnemyData.NameLength;
                    col = tc;
                }
                col.HeaderText = c.Header;
                col.Width = c.Width;
                col.ToolTipText = c.Tip;
                col.Tag = c;
                bool numeric = c.Kind == K_ID || c.Kind == K_BYTE || c.Kind == K_WORD || c.Kind == K_STAT2 || c.Kind == K_CHANCE;
                if (numeric) col.ValueType = typeof(int);
                if (c.Kind == K_EXPHP || c.Kind == K_REALDROP) col.ValueType = typeof(double);
                if (c.Kind == K_ID) col.DefaultCellStyle.Format = "X2";
                if (c.Kind == K_EXPHP) col.DefaultCellStyle.Format = "0.00";
                if (c.Kind == K_REALDROP) col.DefaultCellStyle.Format = "0.##'%'";
                if (numeric || c.Kind == K_EXPHP || c.Kind == K_REALDROP)
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                bool readOnly = c.Kind == K_ID || c.Kind == K_EXPHP || c.Kind == K_REALDROP || c.Kind == K_ELEM || c.Kind == K_ATKELEM;
                col.ReadOnly = readOnly;
                if (readOnly && c.Kind != K_ID) col.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                col.SortMode = (c.Kind == K_DROPITEM || c.Kind == K_AI || c.Kind == K_WHERE) ? DataGridViewColumnSortMode.NotSortable : DataGridViewColumnSortMode.Automatic;
                grid.Columns.Add(col);
            }
            grid.Columns[1].Frozen = true;

            grid.CellValueChanged += new DataGridViewCellEventHandler(grid_CellValueChanged);
            grid.CurrentCellDirtyStateChanged += delegate
            {
                // Combo boxes: apply the choice as soon as it's picked.
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewComboBoxCell)
                    grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.DataError += delegate(object s, DataGridViewDataErrorEventArgs e)
            {
                e.ThrowException = false;
                e.Cancel = false;
                grid.CancelEdit();
            };
            grid.CellContentClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                Col c = (Col)grid.Columns[e.ColumnIndex].Tag;
                if (c.Kind == K_AI && OpenAI != null) OpenAI((int)grid.Rows[e.RowIndex].Tag);
                if (c.Kind == K_WHERE && OpenWhere != null) OpenWhere((int)grid.Rows[e.RowIndex].Tag);
            };
            Controls.Add(UiHelpers.Anchor(this, grid, AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right));
        }

        public void LoadRom(EnemyData enemyData, ItemData itemData)
        {
            enemies = enemyData;
            items = itemData;
            loading = true;

            dropNames.Clear();
            dropNames.Add("(none)");
            for (int id = 1; id < ItemData.Count; id++)
                dropNames.Add((items != null ? items.GetName(id).Trim() : "Item") + " (" + id.ToString("X3") + ")");
            DataGridViewComboBoxColumn dc = (DataGridViewComboBoxColumn)grid.Columns[IndexOfKind(K_DROPITEM)];
            dc.Items.Clear();
            foreach (string n in dropNames) dc.Items.Add(n);
            dc.Items.Add(NoDropSlot);

            grid.Rows.Clear();
            for (int i = 0; i < EnemyData.Count; i++)
            {
                int r = grid.Rows.Add();
                grid.Rows[r].Tag = i;
                FillRow(grid.Rows[r]);
            }
            loading = false;
            this.Enabled = true;
            ApplyFilter();
        }

        public void Disable()
        {
            enemies = null;
            grid.Rows.Clear();
            this.Enabled = false;
        }

        private int IndexOfKind(int kind)
        {
            for (int k = 0; k < Cols.Length; k++) if (Cols[k].Kind == kind) return k;
            return -1;
        }

        public void CommitCurrent()
        {
            if (enemies != null && grid.IsCurrentCellInEditMode) grid.EndEdit();
        }

        /// <summary>Re-reads every row from the ROM data (after edits on other tabs).</summary>
        public void RefreshView()
        {
            if (enemies == null) return;
            CommitCurrent();
            bool was = loading;
            loading = true;
            foreach (DataGridViewRow row in grid.Rows) FillRow(row);
            loading = was;
            ApplyFilter();
        }

        /// <summary>Re-reads one enemy (after the original Enemies tab saved it).</summary>
        public void RefreshEnemy(int index)
        {
            if (enemies == null) return;
            bool was = loading;
            loading = true;
            foreach (DataGridViewRow row in grid.Rows)
                if ((int)row.Tag == index) FillRow(row);
            loading = was;
        }

        private void FillRow(DataGridViewRow row)
        {
            int i = (int)row.Tag;
            for (int k = 0; k < Cols.Length; k++)
            {
                Col c = Cols[k];
                DataGridViewCell cell = row.Cells[k];
                switch (c.Kind)
                {
                    case K_ID: cell.Value = i; break;
                    case K_NAME: cell.Value = enemies.GetName(i); break;
                    case K_BYTE: cell.Value = enemies.GetByte(i, c.Offset); break;
                    case K_WORD: cell.Value = enemies.GetWord(i, c.Offset); break;
                    case K_STAT2: cell.Value = enemies.GetByte(i, c.Offset) * 2; break;
                    case K_DROPITEM:
                        if (enemies.HasDrop(i))
                        {
                            int item = enemies.GetDropItem(i);
                            cell.Value = item < dropNames.Count ? dropNames[item] : dropNames[0];
                            cell.ReadOnly = false;
                        }
                        else
                        {
                            cell.Value = NoDropSlot;
                            cell.ReadOnly = true;
                            cell.Style.ForeColor = SystemColors.GrayText;
                        }
                        break;
                    case K_CHANCE:
                        cell.Value = enemies.GetDropChance(i);
                        cell.ReadOnly = !enemies.HasDrop(i);
                        if (cell.ReadOnly) cell.Style.ForeColor = SystemColors.GrayText;
                        break;
                    case K_REALDROP: cell.Value = EnemyData.RealDropPercent(enemies.GetDropChance(i)); break;
                    case K_EXPHP:
                        {
                            int hp = enemies.GetWord(i, EnemyData.OFS_HP);
                            cell.Value = hp > 0 ? (double)enemies.GetWord(i, EnemyData.OFS_EXP) / hp : 0.0;
                        }
                        break;
                    case K_ELEM:
                        cell.Value = EnemyElements.Summary(enemies.RawData,
                            EnemyElements.Find(enemies.RawData, enemies.ScriptStart(i), enemies.RecordEnd(i)));
                        break;
                    case K_ATKELEM:
                        {
                            List<string> l = new List<string>();
                            foreach (int v in enemies.AttackElements(i)) l.Add(ScriptTools.DescribeElement(v));
                            cell.Value = string.Join(", ", l.ToArray());
                        }
                        break;
                }
            }
            Highlight(row);
        }

        // ---------------- editing ----------------

        private void grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (loading || enemies == null || e.RowIndex < 0 || e.ColumnIndex < 0) return;
            DataGridViewRow row = grid.Rows[e.RowIndex];
            int i = (int)row.Tag;
            Col c = Cols[e.ColumnIndex];
            object value = row.Cells[e.ColumnIndex].Value;
            int n = 0;
            if (value is int) n = (int)value;
            else if (value != null) int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            if (n < 0) n = 0;
            if (c.Max > 0 && n > c.Max) n = c.Max;

            switch (c.Kind)
            {
                case K_NAME: enemies.SetName(i, value == null ? "" : value.ToString()); break;
                case K_BYTE: enemies.SetByte(i, c.Offset, n); break;
                case K_WORD: enemies.SetWord(i, c.Offset, n); break;
                case K_STAT2: enemies.SetByte(i, c.Offset, n / 2); break;
                case K_CHANCE: enemies.SetDrop(i, enemies.GetDropItem(i), n); break;
                case K_DROPITEM:
                    {
                        int item = dropNames.IndexOf(value == null ? "" : value.ToString());
                        if (item >= 0) enemies.SetDrop(i, item, enemies.GetDropChance(i));
                    }
                    break;
                default: return;
            }

            // Show what was actually stored (clamped, rounded), plus the computed columns.
            loading = true;
            FillRow(row);
            loading = false;
            if (EnemyEdited != null) EnemyEdited(i);
        }

        // ---------------- change highlighting ----------------

        private static object RefValue(EnemyData en, int i, Col c)
        {
            byte[] r = Reference.Data;
            if (r == null) return null;
            int rec = Reference.Shift(en.RecordOffset(i));
            if (!Reference.InRange(rec, 0x24)) return null;
            switch (c.Kind)
            {
                case K_NAME:
                    {
                        char[] ch = new char[EnemyData.NameLength];
                        for (int k = 0; k < ch.Length; k++) ch[k] = (char)r[rec + k];
                        return new string(ch).TrimEnd(' ', '\0');
                    }
                case K_BYTE: return (int)r[rec + c.Offset];
                case K_WORD: return r[rec + c.Offset] | (r[rec + c.Offset + 1] << 8);
                case K_STAT2: return r[rec + c.Offset] * 2;
                case K_DROPITEM:
                case K_CHANCE:
                    {
                        int o = en.DropOffset(i);
                        if (o < 0) return null;
                        int ro = Reference.Shift(o);
                        if (!Reference.InRange(ro, 2) || r[ro - 1] != 0x03) return null;
                        int w = r[ro] | (r[ro + 1] << 8);
                        return c.Kind == K_CHANCE ? (object)(w >> 9) : (object)(w & 0x1FF);
                    }
            }
            return null;
        }

        private void Highlight(DataGridViewRow row)
        {
            int i = (int)row.Tag;
            for (int k = 0; k < Cols.Length; k++)
            {
                Col c = Cols[k];
                DataGridViewCell cell = row.Cells[k];
                object orig = RefValue(enemies, i, c);
                bool changed = false;
                if (orig != null)
                {
                    object now = cell.Value;
                    if (c.Kind == K_DROPITEM) now = dropNames.IndexOf(now == null ? "" : now.ToString());
                    if (c.Kind == K_NAME) changed = !string.Equals((string)orig, now == null ? "" : now.ToString().TrimEnd(' '));
                    else changed = now == null || !orig.Equals(now);
                }
                cell.Style.BackColor = changed ? ChangeHighlighter.ChangedColor : Color.Empty;
                if (changed) cell.ToolTipText = "Original: " + OrigText(c, orig);
                else cell.ToolTipText = "";
            }
        }

        private string OrigText(Col c, object orig)
        {
            if (c.Kind == K_DROPITEM)
            {
                int id = (int)orig;
                return id < dropNames.Count ? dropNames[id] : "#" + id;
            }
            return orig.ToString();
        }

        // ---------------- find ----------------

        private void ApplyFilter()
        {
            if (enemies == null) return;
            string f = textFind.Text.Trim();
            grid.CurrentCell = null;
            int shown = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool show = f.Length == 0;
                if (!show)
                {
                    foreach (int k in new int[] { IndexOfKind(K_NAME), IndexOfKind(K_ELEM), IndexOfKind(K_ATKELEM), IndexOfKind(K_DROPITEM) })
                    {
                        object v = row.Cells[k].Value;
                        if (v != null && v.ToString().IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) { show = true; break; }
                    }
                }
                row.Visible = show;
                if (show) shown++;
            }
            labelCount.Text = shown + " of " + grid.Rows.Count + " enemies";
        }
    }
}
