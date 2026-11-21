using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using com.lufiaIIEditor.RomInfo;

namespace com.lufiaIIEditor.UI
{
    public partial class Form1 : Form
    {
        private Rom romData;
        private string selectedRom;
        private int prevValueEnemy = 1;
        private int prevValueSpell = 1;
        private int prevValueSkill = 1;
        private bool canSave = false;
        /// <summary>The original tabs are hidden; their controls must never write to the ROM.</summary>
        private static readonly bool legacyTabs = false;
        private static int maxEnemies = 224;
        private static int maxItems = 467;
        private static int maxSpells = 40;
        private string[] enemyNames = new string[maxEnemies];
        private string[] itemNames = new string[maxItems];
        private string[] spellNames = new string[maxSpells];

        private int prevEnemyItem = 0;
        private int prevSpellItem = 0;
        private int prevDropItem = 0;
        private int prevSkillNameItem = 0;

        private CapsuleMonsterTab capsuleTab;
        private EquipmentTab equipmentTab;
        private ItemsTab itemsTab;
        private IPTab ipTab;
        private EnemyStatusTab enemyStatusTab;
        private EnemyGridTab enemyGridTab;
        private DescriptionsTab descriptionsTab;
        private EncountersTab encountersTab;
        private DialogueTab dialogueTab;
        private ToolStripMenuItem expandItem, shrinkItem;
        private SpellEffectsTab spellEffectsTab;
        private CharactersTab charactersTab;
        private PartyTab partyTab;
        private ShopsTab shopsTab;
        private CaveLootTab caveLootTab;
        private CapsuleAITab capsuleAITab;
        private CapsuleFeedingTab capsuleFeedingTab;
        private ChestsTab chestsTab;
        private GameFixesTab gameFixesTab;
        private ToolStripLabel compareLabel;

        public Form1()
        {
            InitializeComponent();
            charactersTab = new CharactersTab();
            partyTab = new PartyTab();
            equipmentTab = new EquipmentTab();
            itemsTab = new ItemsTab();
            shopsTab = new ShopsTab();
            caveLootTab = new CaveLootTab();
            chestsTab = new ChestsTab();
            enemyGridTab = new EnemyGridTab();
            enemyStatusTab = new EnemyStatusTab();
            encountersTab = new EncountersTab();
            spellEffectsTab = new SpellEffectsTab();
            ipTab = new IPTab();
            capsuleTab = new CapsuleMonsterTab();
            capsuleAITab = new CapsuleAITab();
            capsuleFeedingTab = new CapsuleFeedingTab();
            descriptionsTab = new DescriptionsTab();
            dialogueTab = new DialogueTab();
            gameFixesTab = new GameFixesTab();
            gameFixesTab.FixesChanged += delegate(object s, EventArgs ev)
            {
                // A fix can change bytes other tabs show (enemy scripts, item records, names):
                // re-read them so the next commit doesn't write the old values back.
                refreshAllViews();
            };

            enemyGridTab.EnemyEdited += delegate(int i)
            {
                // Keep the Enemy AI tab's enemy list in step with grid edits.
                enemyStatusTab.RefreshNames();
            };
            enemyGridTab.OpenWhere += delegate(int i)
            {
                if (!encountersTab.Enabled) return;
                this.tabControl1.SelectedTab = encountersTab;
                encountersTab.FindEnemy(i);
            };
            enemyGridTab.OpenAI += delegate(int i)
            {
                this.tabControl1.SelectedTab = enemyStatusTab;
                enemyStatusTab.SelectEnemy(i);
            };

            // Tab order: the party and its gear, then where gear comes from, then enemies and
            // what they use against you, then capsule monsters, then the game's text, then bug fixes.
            this.tabControl1.TabPages.AddRange(new TabPage[] {
                charactersTab, partyTab, equipmentTab, itemsTab, shopsTab, chestsTab, caveLootTab,
                enemyGridTab, enemyStatusTab, encountersTab, spellEffectsTab, ipTab,
                capsuleTab, capsuleAITab, capsuleFeedingTab, descriptionsTab, dialogueTab, gameFixesTab });
            // zanto's original Enemies / Spells / Enemy Skills tabs: everything they edited now
            // lives in the new tabs (Enemy Overview, Enemy AI, Spell Effects), so they're taken out
            // of the tab strip. Their controls stay alive but are never written back to the ROM
            // (see updateEnemyValues / updateSpellValues / updateSkillValues).
            this.tabControl1.TabPages.Remove(this.tabPage1);
            this.tabControl1.TabPages.Remove(this.tabPage2);
            this.tabControl1.TabPages.Remove(this.tabPage3);
            this.tabControl1.Deselecting += new TabControlCancelEventHandler(tabControl1_Deselecting);
            this.Text = AppInfo.Title;

            // Resizable window: the new tabs' big lists stretch with it (see UiHelpers.Relayout).
            Size originalClient = this.ClientSize;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.ClientSize = originalClient;
            this.MinimumSize = this.Size;
            this.tabControl1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            EventHandler relayout = delegate(object s2, EventArgs e2)
            {
                UiHelpers.WindowGrowth = new Size(Math.Max(0, this.ClientSize.Width - originalClient.Width),
                                                  Math.Max(0, this.ClientSize.Height - originalClient.Height));
                UiHelpers.Relayout(this.tabControl1.SelectedTab);
            };
            this.Resize += relayout;
            this.tabControl1.SelectedIndexChanged += relayout;
            this.Load += relayout;

            // Comparison with a clean ROM (changed fields turn blue).
            ToolStripMenuItem setClean = new ToolStripMenuItem("Set clean ROM to compare against...");
            setClean.Click += new EventHandler(setCleanRom_Click);
            ToolStripMenuItem stopClean = new ToolStripMenuItem("Stop comparing to the clean ROM");
            stopClean.Click += new EventHandler(stopCleanRom_Click);
            int at = this.fileToolStripMenuItem.DropDownItems.IndexOf(this.exitToolStripMenuItem);
            if (at < 0) at = this.fileToolStripMenuItem.DropDownItems.Count;
            this.fileToolStripMenuItem.DropDownItems.Insert(at, new ToolStripSeparator());
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 1, setClean);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 2, stopClean);
            ToolStripMenuItem repairSkills = new ToolStripMenuItem("Repair enemy skill names from clean ROM");
            repairSkills.Click += new EventHandler(repairSkillNames_Click);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 3, repairSkills);
            ToolStripMenuItem makePatch = new ToolStripMenuItem("Create patch (BPS / IPS)...");
            makePatch.Click += new EventHandler(makePatch_Click);
            ToolStripMenuItem report = new ToolStripMenuItem("Show changes report...");
            report.Click += new EventHandler(report_Click);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 4, new ToolStripSeparator());
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 5, makePatch);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 6, report);
            ToolStripMenuItem check = new ToolStripMenuItem("Check ROM for problems...");
            check.Click += new EventHandler(check_Click);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 7, check);
            ToolStripMenuItem restore = new ToolStripMenuItem("Restore from backup...");
            restore.Click += new EventHandler(restore_Click);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 8, restore);
            expandItem = new ToolStripMenuItem("Expand ROM (more space for descriptions)...");
            expandItem.Click += new EventHandler(expand_Click);
            shrinkItem = new ToolStripMenuItem("Move descriptions back into the original space...");
            shrinkItem.Click += new EventHandler(shrink_Click);
            expandItem.Enabled = shrinkItem.Enabled = false;
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 9, new ToolStripSeparator());
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 10, expandItem);
            this.fileToolStripMenuItem.DropDownItems.Insert(at + 11, shrinkItem);
            compareLabel = new ToolStripLabel("");
            compareLabel.Alignment = ToolStripItemAlignment.Right;
            compareLabel.ForeColor = SystemColors.GrayText;
            ToolStripMenuItem helpMenu = new ToolStripMenuItem("Help");
            ToolStripMenuItem aboutItem = new ToolStripMenuItem("About " + AppInfo.Name + "...");
            aboutItem.Click += new EventHandler(about_Click);
            helpMenu.DropDownItems.Add(aboutItem);
            this.menuStrip1.Items.Add(helpMenu);
            this.menuStrip1.Items.Add(compareLabel);
            this.tabControl1.SelectedIndexChanged += new EventHandler(tabControl1_SelectedIndexChanged);
            // Warn about unsaved changes when closing (window X, Alt+F4, File > Exit).
            this.FormClosing += new FormClosingEventHandler(Form1_FormClosing);
            this.exitToolStripMenuItem.Click += delegate { this.Close(); };   // had no handler before

            // Item drop chance as the game uses it (see EnemyData): 0-100, then 1 in 4 on top.
            this.numericUpDownProb.Maximum = 100;
            this.label18.Text = "Chance:";
            this.label19.AutoSize = true;
            this.label19.Text = "of 100, then 1 in 4 (100 = 25%)";
        }

        /// <summary>Loads the new tabs (equipment, items, IP moves) from the opened ROM.</summary>
        private void loadExtraTabs()
        {
            capsuleTab.LoadRom(this.romData);
            {
                CapsuleMonsterData capsules = new CapsuleMonsterData(this.romData.RawData, this.romData.HasHeader);
                CapsuleSkillData capsuleSkills = new CapsuleSkillData(this.romData.RawData, this.romData.HasHeader, capsules);
                string capProblem = capsules.Validate();
                if (capProblem == null) capProblem = capsuleSkills.Validate();
                if (capProblem == null) capsuleAITab.LoadRom(capsuleSkills, this.romData.SpellNames);
                else capsuleAITab.Disable();
            }
            // Patches like Fixxxer Deluxe move the enemy records and skill names elsewhere; the enemy
            // tabs would then read (and write) the emptied original space.
            string relocated = GameFixes.RelocationWarning(this.romData.RawData, this.romData.HasHeader);
            EnemyData enemyCheck = new EnemyData(this.romData.RawData, this.romData.HasHeader);
            if (relocated == null && enemyCheck.Validate() == null) enemyStatusTab.LoadRom(this.romData);
            else enemyStatusTab.Disable();
            if (relocated != null)
                MessageBox.Show(relocated, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            gameFixesTab.LoadRom(this.romData);

            CharacterData characters = new CharacterData(this.romData.RawData, this.romData.HasHeader);
            string charProblem = characters.Validate();
            if (charProblem == null) charactersTab.LoadRom(characters);
            else
            {
                charactersTab.Disable();
                MessageBox.Show("Character editing is disabled for this ROM:\n" + charProblem,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            ItemData items = new ItemData(this.romData.RawData, this.romData.HasHeader);
            IPData ip = new IPData(this.romData.RawData, this.romData.HasHeader);
            string problem = items.Validate();
            if (problem == null) problem = ip.Validate();
            EnemyData enemyData = new EnemyData(this.romData.RawData, this.romData.HasHeader);
            bool enemiesOk = relocated == null && enemyData.Validate() == null;
            if (enemiesOk) enemyGridTab.LoadRom(enemyData, problem == null ? items : null);
            else enemyGridTab.Disable();
            EncounterData encounters = new EncounterData(this.romData.RawData, this.romData.HasHeader);
            try
            {
                if (enemiesOk && encounters.Validate() == null) encountersTab.LoadRom(encounters, enemyData);
                else encountersTab.Disable();
            }
            catch (Exception)
            {
                // A problem in one tab shouldn't stop the rest of the ROM from loading.
                encountersTab.Disable();
            }
            spellEffectsTab.LoadRom(this.romData, problem == null ? ip : null);
            if (problem != null)
            {
                equipmentTab.Disable();
                itemsTab.Disable();
                ipTab.Disable();
                shopsTab.Disable();
                caveLootTab.Disable();
                chestsTab.Disable();
                capsuleFeedingTab.Disable();
                partyTab.Disable();
                descriptionsTab.Disable();
                MessageBox.Show("Item / IP editing is disabled for this ROM:\n" + problem,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            equipmentTab.LoadRom(items, ip);
            itemsTab.LoadRom(items);
            ipTab.SetItems(items);
            ipTab.LoadRom(ip, this.romData.SpellNames);
            DescriptionData descriptions = new DescriptionData(this.romData.RawData, this.romData.HasHeader);
            bool descOk = descriptions.Load() == null;
            if (descOk) descriptionsTab.LoadRom(descriptions, items, ip, this.romData);
            else descriptionsTab.Disable();
            try
            {
                DialogueData dialogue = new DialogueData(this.romData.RawData, this.romData.HasHeader);
                if (dialogue.Load() == null) dialogueTab.LoadRom(dialogue);
                else dialogueTab.Disable();
            }
            catch (Exception) { dialogueTab.Disable(); }
            expandItem.Enabled = descOk && RomExpansion.Size(this.romData.RawData, this.romData.HasHeader) < RomExpansion.MaxSize;
            shrinkItem.Enabled = descOk && descriptions.IsMoved;

            ShopData shops = new ShopData(this.romData.RawData, this.romData.HasHeader);
            string shopProblem = shops.Load();
            PartyData party = new PartyData(this.romData.RawData, this.romData.HasHeader);
            if (charProblem == null && party.Load() == null) partyTab.LoadRom(party, characters, items, this.romData);
            else partyTab.Disable();

            FeedingData feeding = new FeedingData(this.romData.RawData, this.romData.HasHeader);
            if (feeding.Load() == null) capsuleFeedingTab.LoadRom(feeding, items);
            else capsuleFeedingTab.Disable();

            CaveLootData cave = new CaveLootData(this.romData.RawData, this.romData.HasHeader);
            string caveProblem = cave.Validate();
            ChestData chestData = new ChestData(this.romData.RawData, this.romData.HasHeader);
            string chestProblem = chestData.Load();
            if (chestProblem == null) chestsTab.LoadRom(chestData, items);
            else
            {
                chestsTab.Disable();
                MessageBox.Show("Treasure chest editing is disabled for this ROM:\n" + chestProblem,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (caveProblem == null) caveLootTab.LoadRom(cave, items);
            else
            {
                caveLootTab.Disable();
                MessageBox.Show("Ancient Cave loot editing is disabled for this ROM:\n" + caveProblem,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (shopProblem == null) shopsTab.LoadRom(shops, items, this.romData.SpellNames);
            else
            {
                shopsTab.Disable();
                MessageBox.Show("Shop editing is disabled for this ROM:\n" + shopProblem,
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------------- comparison with a clean ROM ----------------

        private void updateCompareLabel()
        {
            if (compareLabel == null) return;
            if (this.romData == null) { compareLabel.Text = ""; return; }
            compareLabel.Text = Reference.IsCleanRom
                ? "Blue = changed from clean ROM (" + Path.GetFileName(Reference.CleanPath) + ")"
                : "Blue = changed since opening (File > Set clean ROM to compare with the original game)";
        }

        private void setCleanRom_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Title = "Choose an unmodified Lufia II ROM";
            dlg.Filter = "SNES ROM (*.smc;*.sfc)|*.smc;*.sfc|All files (*.*)|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            if (this.romData == null)
            {
                Reference.Remember(dlg.FileName);
                MessageBox.Show("Saved. It will be used when you open a ROM.", "Clean ROM");
                return;
            }
            string problem = Reference.TryLoad(dlg.FileName);
            if (problem != null)
            {
                MessageBox.Show(problem, "Clean ROM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Reference.Remember(dlg.FileName);
            refreshComparison();
        }

        private void stopCleanRom_Click(object sender, EventArgs e)
        {
            if (this.romData == null) { Reference.Remember(null); return; }
            byte[] onDisk;
            try { onDisk = File.ReadAllBytes(selectedRom); }
            catch (Exception) { onDisk = this.romData.RawData; }
            Reference.Forget(onDisk);
            refreshComparison();
        }

        /// <summary>Stores what's on screen, then shows it again so highlights use the new reference.</summary>
        private void refreshComparison()
        {
            updateCompareLabel();
            if (this.romData == null || !canSave) return;
            commitExtraTabs();
            refreshAllViews();
        }

        /// <summary>Shows every tab again from the ROM data (without storing what's on screen first).</summary>
        private void refreshAllViews()
        {
            if (this.romData == null) return;
            capsuleTab.RefreshView();
            capsuleAITab.RefreshView();
            capsuleFeedingTab.RefreshView();
            charactersTab.RefreshView();
            partyTab.RefreshView();
            enemyStatusTab.RefreshView();
            enemyGridTab.RefreshView();
            encountersTab.RefreshView();
            spellEffectsTab.RefreshView();
            equipmentTab.RefreshFromRom();
            itemsTab.RefreshView();
            ipTab.RefreshView();
            shopsTab.RefreshView();
            caveLootTab.RefreshView();
            chestsTab.RefreshView();
            descriptionsTab.RefreshView();
            dialogueTab.RefreshView();
            gameFixesTab.RefreshView();
        }

        /// <summary>
        /// Fixes damage left by the original Enemy Skills tab, which saved each name one slot
        /// too early and padded it with spaces: that could overwrite the 00 ending a name, the
        /// E0 starting the next record and the first letters of the next name, so two names ran
        /// together in game. Restores those separator bytes (and any record whose start was
        /// overwritten) from the clean ROM. Other renames are left alone.
        /// </summary>
        private void repairSkillNames_Click(object sender, EventArgs e)
        {
            if (this.romData == null) return;
            if (!Reference.IsCleanRom)
            {
                MessageBox.Show("Set a clean ROM first (File > Set clean ROM to compare against...).", "Repair skill names");
                return;
            }
            enemyStatusTab.CommitCurrent();

            byte[] cur = this.romData.RawData;
            byte[] cl = Reference.Data;
            int delta = Reference.Shift(0);          // reference offset - current offset
            int table = 0x2F2D7 + (this.romData.HasHeader ? 0x200 : 0);
            int refTable = table + delta;
            int bank = 0x05 * 0x8000 + (this.romData.HasHeader ? 0x200 : 0) + delta - 0x8000;

            int firstRec = bank + (cl[refTable] | (cl[refTable + 1] << 8));
            int count = (firstRec - refTable) / 2;
            if (count < 50 || count > 512)
            {
                MessageBox.Show("The clean ROM's skill table wasn't found where expected; nothing was changed.", "Repair skill names");
                return;
            }
            for (int k = 0; k < count * 2; k++)
            {
                if (cur[table + k] != cl[refTable + k])
                {
                    MessageBox.Show("Your ROM's skill name table points to different places than the clean ROM's,\n" +
                        "so it can't be repaired automatically. Nothing was changed.", "Repair skill names");
                    return;
                }
            }

            int fixedRecords = 0, fixedEnds = 0;
            for (int k = 0; k < count; k++)
            {
                int r = bank + (cl[refTable + 2 * k] | (cl[refTable + 2 * k + 1] << 8));
                int len = 0;
                while (r + 1 + len < cl.Length && cl[r + 1 + len] != 0 && len < 64) len++;
                int c = r - delta;
                if (cur[c] != cl[r])
                {
                    // The record's start was overwritten: put the whole record back.
                    for (int i = 0; i <= len + 1; i++) cur[c + i] = cl[r + i];
                    fixedRecords++;
                }
                else if (cur[c + 1 + len] != 0)
                {
                    cur[c + 1 + len] = 0;
                    fixedEnds++;
                }
            }

            int keep = comboBoxSkillName.SelectedIndex;
            this.romData.loadSkillNames();
            canSave = false;
            loadSkillNames();
            if (keep >= 0 && keep < comboBoxSkillName.Items.Count) comboBoxSkillName.SelectedIndex = keep;
            prevValueSkill = comboBoxSkillName.SelectedIndex;
            if (comboBoxSkillName.SelectedIndex >= 0) updateSkillFields(comboBoxSkillName.SelectedIndex);
            canSave = true;
            enemyStatusTab.RefreshSkillNames();
            spellEffectsTab.RefreshView();

            MessageBox.Show(fixedRecords + fixedEnds == 0
                ? "No damaged skill names found."
                : "Repaired " + (fixedRecords + fixedEnds) + " skill name(s) (" + fixedEnds + " name endings, " +
                  fixedRecords + " overwritten names).\nSave the ROM to keep the repair.", "Repair skill names");
        }

        private static string RefString(int offset, int length)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < length; i++) sb.Append((char)Reference.Data[offset + i]);
            return sb.ToString();
        }

        private void highlightEnemy(int ind)
        {
            if (this.romData == null || ind < 0) return;
            int ro = Reference.Shift(int.Parse(this.romData.getEnemyOffset(ind), NumberStyles.HexNumber));
            bool ok = Reference.InRange(ro, 0x24);
            ChangeHighlighter.Set(this.textBoxName, ok ? (object)RefString(ro, 13) : null);

            NumericUpDown[] nums = { this.numericUpLevel, this.numericUpSprite, this.numericUpPattern, this.numericUpHP,
                this.numericUpMP, this.numericUpATP, this.numericUpDFP, this.numericUpAGL, this.numericUpINT,
                this.numericUpGUT, this.numericUpMGR, this.numericUpDownEXP, this.numericUpDownGOLD };
            string[] onebyte = { "D", "F", "10", "19", "1A", "1B", "1C" };
            foreach (NumericUpDown n in nums)
            {
                string tag = n.Tag.ToString();
                int o = ro + int.Parse(tag, NumberStyles.HexNumber);
                bool one = Array.IndexOf(onebyte, tag) >= 0;
                object orig = null;
                if (ok) orig = one ? Reference.Data[o] : (Reference.Data[o] | (Reference.Data[o + 1] << 8));
                ChangeHighlighter.Set(n, orig);
            }

            // Item drop, decoded the same way updateEnemyFields does it.
            object origDrop = null, origProb = null;
            if (ok && this.comboBoxDrop.Enabled && Reference.Data[ro + 0x21] == 3)
            {
                int v1 = Reference.Data[ro + 0x23];
                int high = (v1 & 1) != 0 ? 0x100 : 0;
                int v2 = Reference.Data[ro + 0x22] + high;
                if (v2 < this.comboBoxDrop.Items.Count) origDrop = v2;
                origProb = v1 >> 1;   // the game's 0-99 roll; see EnemyData
            }
            ChangeHighlighter.Set(this.comboBoxDrop, origDrop);
            ChangeHighlighter.Set(this.numericUpDownProb, origProb);
        }

        private void highlightSpell(int ind)
        {
            if (this.romData == null || ind < 0) return;
            int ro = Reference.Shift(int.Parse(this.romData.getSpellOffset(ind), NumberStyles.HexNumber));
            bool ok = Reference.InRange(ro, 0x13);
            ChangeHighlighter.Set(this.textBoxSpellName, ok ? (object)RefString(ro, 8) : null);
            ChangeHighlighter.Set(this.numericUpDownMPCost, ok ? (object)(Reference.Data[ro + 0xC] | (Reference.Data[ro + 0xD] << 8)) : null);
            ChangeHighlighter.Set(this.numericUpDownGoldCost, ok ? (object)(Reference.Data[ro + 0xF] | (Reference.Data[ro + 0x10] << 8)) : null);
        }

        private void highlightSkill(int ind)
        {
            if (this.romData == null || ind < 0) return;
            int len = this.romData.skillNamesLength[ind];
            int ro = Reference.Shift(int.Parse(this.romData.getSkillOffset(ind), NumberStyles.HexNumber));
            ChangeHighlighter.Set(this.textBoxSkillName, Reference.InRange(ro, len) ? (object)RefString(ro, len) : null);
        }

        // ---------------- patch export and changes report ----------------

        /// <summary>Stores whatever is on screen in every tab (as Save does).</summary>
        private void commitEverything()
        {
            commitExtraTabs();
        }

        private void makePatch_Click(object sender, EventArgs e)
        {
            if (this.romData == null || !canSave) { MessageBox.Show("Open a ROM first.", "Create patch"); return; }
            if (!Reference.IsCleanRom)
            {
                MessageBox.Show("A patch is made against the clean (unmodified) ROM, so set it first:\nFile > Set clean ROM to compare against...",
                    "Create patch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                commitEverything();
                byte[] clean = Reference.Data;
                byte[] edited = PatchTools.MatchHeader(this.romData.RawData, this.romData.HasHeader, clean, Reference.HasHeader);
                int changed = 0;
                for (int i = 0; i < Math.Min(clean.Length, edited.Length); i++) if (clean[i] != edited[i]) changed++;
                if (changed == 0 && clean.Length == edited.Length) { MessageBox.Show("No changes from the clean ROM, so there's nothing to patch.", "Create patch"); return; }

                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Title = "Save patch";
                dlg.Filter = "BPS patch (recommended)|*.bps|IPS patch|*.ips";
                dlg.FileName = Path.GetFileNameWithoutExtension(selectedRom);
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                bool ips = dlg.FilterIndex == 2 || dlg.FileName.EndsWith(".ips", StringComparison.OrdinalIgnoreCase);
                byte[] patch = ips ? PatchTools.MakeIps(clean, edited) : PatchTools.MakeBps(clean, edited);
                File.WriteAllBytes(dlg.FileName, patch);
                MessageBox.Show("Patch saved (" + patch.Length + " bytes, " + changed + " bytes changed).\n\n" +
                    "It applies to the clean ROM you set (" + Path.GetFileName(Reference.CleanPath) + "), which is " +
                    (Reference.HasHeader ? "a headered ROM (with the 512-byte copier header)" : "an unheadered ROM") + ".\n" +
                    "Unsaved edits are included. " + (ips ? "" : "BPS patches check the ROM, so patchers like Floating IPS warn if someone uses the wrong one."),
                    "Create patch", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception x)
            {
                MessageBox.Show("Couldn't create the patch: " + x.Message, "Create patch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void report_Click(object sender, EventArgs e)
        {
            if (this.romData == null || !canSave) { MessageBox.Show("Open a ROM first.", "Changes report"); return; }
            commitEverything();
            string text = ChangeReport.Build(this.romData);
            if (!Reference.IsCleanRom)
                text = "(Tip: set the clean ROM with File > Set clean ROM... to see every change from the original game.)" +
                       Environment.NewLine + Environment.NewLine + text;
            ShowTextWindow("Changes report", text, " changes.txt");
        }

        private void expand_Click(object sender, EventArgs e)
        {
            ChangeRomSize(true);
        }

        private void shrink_Click(object sender, EventArgs e)
        {
            ChangeRomSize(false);
        }

        /// <summary>Expands the ROM and moves the descriptions there, or the reverse; then saves and reopens.</summary>
        /// <summary>
        /// Copies the ROM as it is on disk into the backups folder before it gets overwritten.
        /// False means the caller shouldn't write (the backup failed and the user said no).
        /// </summary>
        private bool BackupBeforeWriting(string label)
        {
            string error;
            BackupTools.Make(selectedRom, label, out error);
            if (error == null) return true;
            return MessageBox.Show(
                "The backup couldn't be made:\n" + error + "\n\n" +
                "Save anyway? The ROM on disk would be overwritten with no way back.",
                AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private void about_Click(object sender, EventArgs e)
        {
            Form f = new Form();
            f.Text = "About " + AppInfo.Name;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(440, 282);

            Label title = new Label();
            title.Text = AppInfo.Title;
            title.Font = new Font(f.Font.FontFamily, 12f, FontStyle.Bold);
            title.Location = new Point(16, 14);
            title.AutoSize = true;
            f.Controls.Add(title);

            Label body = new Label();
            body.Text = "An expanded editor for Lufia II: Rise of the Sinistrals.\n\n" +
                "Built on the Lufia II Editor by " + AppInfo.OriginalBy + ". The enemy, spell and\n" +
                "enemy skill editing, the ROM loading and the offset files this\n" +
                "editor still uses are his work.\n\n" +
                "Everything added in this version by " + AppInfo.AddedBy + ".\n\n" +
                "Game Fixes tab: fixes from " + AppInfo.FixesBy + ".";
            body.Location = new Point(16, 46);
            body.Size = new Size(410, 130);
            f.Controls.Add(body);

            LinkLabel link = new LinkLabel();
            link.Text = AppInfo.OriginalUrl;
            link.Location = new Point(16, 180);
            link.AutoSize = true;
            link.LinkClicked += delegate
            {
                try { System.Diagnostics.Process.Start(AppInfo.OriginalUrl); }
                catch (Exception) { }
            };
            f.Controls.Add(link);

            Label legal = new Label();
            legal.Text = "Lufia II is by Neverland and Taito. This is an unofficial fan tool\n" +
                "and contains no game data: you supply your own ROM.";
            legal.ForeColor = SystemColors.GrayText;
            legal.Location = new Point(16, 208);
            legal.Size = new Size(410, 32);
            f.Controls.Add(legal);

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.Size = new Size(80, 26);
            ok.Location = new Point(344, 246);
            f.Controls.Add(ok);
            f.AcceptButton = f.CancelButton = ok;
            f.ShowDialog(this);
            f.Dispose();
        }

        private void restore_Click(object sender, EventArgs e)
        {
            if (this.romData == null || !canSave) { MessageBox.Show("Open a ROM first.", "Restore from backup"); return; }
            List<FileInfo> list = BackupTools.List(selectedRom);
            if (list.Count == 0)
            {
                MessageBox.Show("There are no backups of this ROM yet.\n\n" +
                    "One is made every time you save, in:\n" + BackupTools.FolderFor(selectedRom),
                    "Restore from backup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Form f = new Form();
            f.Text = "Restore from backup";
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(520, 320);
            f.MinimumSize = f.Size;
            Label head = new Label();
            head.Text = "A copy of " + Path.GetFileName(selectedRom) + " is kept every time you save (the newest " +
                BackupTools.Keep + " are kept). Pick the one to go back to:";
            head.Location = new Point(12, 10);
            head.Size = new Size(496, 32);
            head.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            f.Controls.Add(head);
            ListBox box = new ListBox();
            box.Location = new Point(12, 48);
            box.Size = new Size(496, 200);
            box.IntegralHeight = false;
            box.Font = new Font(FontFamily.GenericMonospace, 9f);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            foreach (FileInfo fi in list) box.Items.Add(BackupTools.Describe(fi));
            box.SelectedIndex = 0;
            f.Controls.Add(box);
            Button folder = new Button();
            folder.Text = "Open folder";
            folder.Size = new Size(100, 26);
            folder.Location = new Point(12, 258);
            folder.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            folder.Click += delegate
            {
                try { System.Diagnostics.Process.Start(BackupTools.FolderFor(selectedRom)); }
                catch (Exception) { MessageBox.Show("Couldn't open " + BackupTools.FolderFor(selectedRom) + "."); }
            };
            f.Controls.Add(folder);
            Button ok = new Button(), cancel = new Button();
            ok.Text = "Restore"; cancel.Text = "Cancel";
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            ok.Size = cancel.Size = new Size(90, 26);
            ok.Location = new Point(318, 258); cancel.Location = new Point(418, 258);
            ok.Anchor = cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            f.Controls.Add(ok); f.Controls.Add(cancel);
            f.AcceptButton = ok; f.CancelButton = cancel;
            DialogResult res = f.ShowDialog(this);
            int pick = box.SelectedIndex;
            f.Dispose();
            if (res != DialogResult.OK || pick < 0 || pick >= list.Count) return;

            if (MessageBox.Show("Put " + Path.GetFileName(selectedRom) + " back to how it was at " +
                list[pick].LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") + "?\n\n" +
                "Everything you've done since then is lost, including unsaved edits. The ROM as it is\n" +
                "now is backed up first, so this can itself be undone.",
                "Restore from backup", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

            try
            {
                if (!BackupBeforeWriting("before restore")) return;
                File.Copy(list[pick].FullName, selectedRom, true);
                reloadRom(null, EventArgs.Empty);
                MessageBox.Show("Restored. The ROM has been reopened.", "Restore from backup",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception x)
            {
                MessageBox.Show("Couldn't restore: " + x.Message, "Restore from backup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Asks 3 MB or 4 MB. Returns 0 if cancelled.</summary>
        private int AskExpandSize(int current)
        {
            Form f = new Form();
            f.Text = "Expand ROM";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MaximizeBox = f.MinimizeBox = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.ClientSize = new Size(470, 205);
            Label l = new Label();
            l.Text = current == RomExpansion.OriginalSize
                ? "Adds empty space to the end of the ROM and moves the item, spell and IP move\n" +
                  "descriptions there (about 31 KB instead of 11 KB). The rest stays free for later.\n" +
                  "Patches made from it still apply to a normal original ROM."
                : "The ROM is already expanded to 3 MB. This adds the rest of the space a LoROM\n" +
                  "cartridge can hold.";
            l.Location = new Point(12, 12);
            l.AutoSize = true;
            f.Controls.Add(l);
            RadioButton r3 = new RadioButton(), r4 = new RadioButton();
            r3.Text = "3 MB (+512 KB) - plenty for the descriptions; a common size";
            r4.Text = "4 MB (+1.5 MB) - the maximum, with no address mirroring";
            r3.Location = new Point(20, 75); r4.Location = new Point(20, 100);
            r3.AutoSize = r4.AutoSize = true;
            r3.Checked = current == RomExpansion.OriginalSize;
            r4.Checked = !r3.Checked;
            r3.Enabled = current == RomExpansion.OriginalSize;
            f.Controls.Add(r3); f.Controls.Add(r4);
            Button ok = new Button(), cancel = new Button();
            ok.Text = "Expand"; cancel.Text = "Cancel";
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            ok.Location = new Point(290, 165); cancel.Location = new Point(375, 165);
            f.Controls.Add(ok); f.Controls.Add(cancel);
            f.AcceptButton = ok; f.CancelButton = cancel;
            DialogResult res = f.ShowDialog(this);
            int size = r3.Checked ? RomExpansion.ExpandedSize : RomExpansion.MaxSize;
            f.Dispose();
            return res == DialogResult.OK ? size : 0;
        }

        private void ChangeRomSize(bool expand)
        {
            if (this.romData == null || !canSave) { MessageBox.Show("Open a ROM first."); return; }
            int current = RomExpansion.Size(this.romData.RawData, this.romData.HasHeader);
            int target = 0;
            if (expand)
            {
                target = AskExpandSize(current);
                if (target == 0) return;
            }
            string what = expand
                ? "The ROM will grow to " + (target / 0x100000) + " MB." + (current == RomExpansion.OriginalSize
                    ? " The original description text is left where it was, and you can undo this later with " +
                      "\"Move descriptions back\" as long as the descriptions fit the original space again." : "") + "\n\n"
                : "This moves the descriptions back into the original space and trims the ROM to 2.5 MB.\n\n";
            what += "The ROM is saved (with your unsaved edits) and reopened. A copy of it as it is now goes " +
                    "into the backups folder first, so File > Restore from backup can undo this." +
                    "\n\nAfter this, check descriptions in game: item menu, equipment, shops, spells and IP moves.";
            if (MessageBox.Show(what, expand ? "Expand ROM" : "Move descriptions back", MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information) != DialogResult.OK) return;

            try
            {
                commitEverything();
                string error;
                byte[] result = expand
                    ? RomExpansion.Expand(this.romData.RawData, this.romData.HasHeader, target, out error)
                    : RomExpansion.Shrink(this.romData.RawData, this.romData.HasHeader, out error);
                if (result == null)
                {
                    MessageBox.Show("Nothing was changed:\n" + error, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string backupError;
                BackupTools.MakeFrom(selectedRom, this.romData.RawData, expand ? "before expanding" : "before shrinking", out backupError);
                if (backupError != null && MessageBox.Show(
                        "The backup couldn't be made:\n" + backupError + "\n\nGo ahead anyway?",
                        AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                File.WriteAllBytes(selectedRom, result);
                reloadRom(null, EventArgs.Empty);
                MessageBox.Show(expand ? "Done. The ROM is now " + (target / 0x100000) + " MB and the descriptions have about 31 KB of space."
                                       : "Done. The descriptions are back in the original space and the ROM is 2.5 MB.",
                    AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception x)
            {
                MessageBox.Show("Something went wrong: " + x.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void check_Click(object sender, EventArgs e)
        {
            if (this.romData == null || !canSave) { MessageBox.Show("Open a ROM first.", "Check ROM"); return; }
            commitEverything();
            string text;
            try { text = RomCheck.Build(this.romData); }
            catch (Exception x) { text = "The check stopped with an error: " + x.Message; }
            ShowTextWindow("Check ROM", text, " check.txt");
        }

        private void ShowTextWindow(string title, string text, string fileSuffix)
        {
            Form f = new Form();
            f.Text = title;
            f.Size = new Size(820, 620);
            f.StartPosition = FormStartPosition.CenterParent;
            TextBox box = new TextBox();
            box.Multiline = true; box.ReadOnly = true; box.ScrollBars = ScrollBars.Both; box.WordWrap = false;
            box.Font = new Font(FontFamily.GenericMonospace, 9f);
            box.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            box.Location = new Point(8, 8);
            box.Size = new Size(f.ClientSize.Width - 16, f.ClientSize.Height - 50);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            f.Controls.Add(box);
            Button save = new Button();
            save.Text = "Save as text...";
            save.Size = new Size(120, 26);
            save.Location = new Point(8, f.ClientSize.Height - 36);
            save.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            save.Click += delegate(object s2, EventArgs e2)
            {
                SaveFileDialog dlg = new SaveFileDialog();
                dlg.Filter = "Text file|*.txt";
                dlg.FileName = Path.GetFileNameWithoutExtension(selectedRom) + fileSuffix;
                if (dlg.ShowDialog(f) == DialogResult.OK) File.WriteAllText(dlg.FileName, box.Text);
            };
            f.Controls.Add(save);
            Button close = new Button();
            close.Text = "Close";
            close.Size = new Size(90, 26);
            close.Location = new Point(f.ClientSize.Width - 98, f.ClientSize.Height - 36);
            close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            close.Click += delegate(object s3, EventArgs e3) { f.Close(); };
            f.Controls.Add(close);
            f.ShowDialog(this);
            f.Dispose();
        }

        private void commitExtraTabs()
        {
            capsuleTab.CommitCurrent();
            capsuleAITab.CommitCurrent();
            capsuleFeedingTab.CommitCurrent();
            charactersTab.CommitCurrent();
            partyTab.CommitCurrent();
            enemyStatusTab.CommitCurrent();
            enemyGridTab.CommitCurrent();
            spellEffectsTab.CommitCurrent();
            equipmentTab.CommitCurrent();
            itemsTab.CommitCurrent();
            ipTab.CommitCurrent();
            shopsTab.CommitCurrent();
            chestsTab.CommitCurrent();
            descriptionsTab.CommitCurrent();
            dialogueTab.CommitCurrent();
        }

        private void tabControl1_Deselecting(object sender, TabControlCancelEventArgs e)
        {
            // Store what's on the tab being left, so other tabs see the latest names.
            commitExtraTabs();
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (this.tabControl1.SelectedTab == equipmentTab) equipmentTab.RefreshFromRom();
            if (this.tabControl1.SelectedTab == enemyGridTab) enemyGridTab.RefreshView();
            if (this.tabControl1.SelectedTab == encountersTab) encountersTab.RefreshView();
            if (this.tabControl1.SelectedTab == enemyStatusTab) enemyStatusTab.RefreshNames();
            if (this.tabControl1.SelectedTab == shopsTab) shopsTab.RefreshView();
            if (this.tabControl1.SelectedTab == caveLootTab) caveLootTab.RefreshView();
            if (this.tabControl1.SelectedTab == chestsTab) chestsTab.RefreshView();
            if (this.tabControl1.SelectedTab == capsuleAITab) capsuleAITab.RefreshView();
            if (this.tabControl1.SelectedTab == partyTab) partyTab.RefreshView();
            if (this.tabControl1.SelectedTab == capsuleFeedingTab) capsuleFeedingTab.RefreshView();
            if (this.tabControl1.SelectedTab == gameFixesTab) gameFixesTab.RefreshView();
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            setFieldsState(false);
        }

        private void loadSkillNames()
        {
            ComboboxItem item;
            comboBoxSkillName.Items.Clear();
            int i = 0;
            foreach (string name in this.romData.skillNames)
            {

                item = new ComboboxItem();
                item.Text = this.romData.skillNames[i];
                item.Value = i + 1;
                comboBoxSkillName.Items.Add(item);
                i++;
            }
        }

        private void loadEnemyNames()
        {
            comboBoxEnemies.Items.Clear();
            string offset;
            ComboboxItem item;
            for (var i = 0; i < maxEnemies; i += 1)
            {
                offset = this.romData.getEnemyOffset(i);
                enemyNames[i] = this.romData.readDataString(offset, 13);
                item = new ComboboxItem();
                item.Text = enemyNames[i];
                item.Value = i;
                comboBoxEnemies.Items.Add(item);
            }
            
        }

        private void loadSpellNames()
        {
            comboBoxSpells.Items.Clear();
            string offset;
            ComboboxItem item;
            for (var i = 0; i < maxSpells; i += 1)
            {
                offset = this.romData.getSpellOffset(i);
                spellNames[i] = this.romData.readDataString(offset, 8);
                item = new ComboboxItem();
                item.Text = spellNames[i];
                item.Value = i;
                comboBoxSpells.Items.Add(item);
            }
        }

        private void loadItemNames()
        {
            comboBoxDrop.Items.Clear();
            string[] lines;
            using (StreamReader sr = new StreamReader(AppInfo.FileBesideExe("LufiaItemOffsets.txt")))
            {
                string text = sr.ReadToEnd();
                lines = text.Split('\n');
            }

            ComboboxItem item;
            string offset, name;
            string[] split;
            foreach(string line in lines) {
                split = line.Split(';');
                offset = split[0];
                name = split[1];
                item = new ComboboxItem();
                item.Text = name.TrimEnd('\r', '\n'); ;
                item.Value = offset.TrimEnd('\r', '\n'); ;
                comboBoxDrop.Items.Add(item);
            }
            comboBoxDrop.SelectedIndex = 0;
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!ConfirmUnsaved("opening another ROM")) return;
            canSave = false;
            try
            {
                System.Windows.Forms.DialogResult result = this.openFileOpenRom.ShowDialog(this);
                if (result.Equals(System.Windows.Forms.DialogResult.OK))
                {
                    if (File.Exists(AppInfo.FileBesideExe("lufia2Aux.smc")))
                    {
                        File.Delete(AppInfo.FileBesideExe("lufia2Aux.smc"));
                    }
                    selectedRom = this.openFileOpenRom.FileName;
                    File.Copy(selectedRom, AppInfo.FileBesideExe("lufia2Aux.smc"));
                    this.romData = new Rom(AppInfo.FileBesideExe("lufia2Aux.smc"));
            Reference.Init(this.romData.RawData, this.romData.HasHeader);
            updateCompareLabel();
                    this.Text = AppInfo.Title + " - " + selectedRom;
                    setFieldsState(true);
                    loadItemNames();
                    loadEnemyNames();
                    loadSpellNames();
                    loadSkillNames();
                    comboBoxEnemies.SelectedIndex = 0;
                    comboBoxDrop.SelectedIndex = 0;
                    comboBoxSpells.SelectedIndex = 0;
                    comboBoxSkillName.SelectedIndex = 0;
                    updateEnemyFields(comboBoxEnemies.SelectedIndex);
                    loadExtraTabs();
                    canSave = true;
                }
            }
            catch (Exception x)
            {
                MessageBox.Show("Error loading rom: " + x.ToString());
            }
        }

        private void reloadRom(object sender, EventArgs e) 
        {
            if (sender != null && HasUnsavedChanges() &&
                MessageBox.Show("Reloading reads the ROM from disk again, so your unsaved changes will be lost.\n\nReload anyway?",
                    AppInfo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                return;
            canSave = false;
            prevEnemyItem = comboBoxEnemies.SelectedIndex;
            prevSpellItem = comboBoxSpells.SelectedIndex;
            prevDropItem = comboBoxDrop.SelectedIndex;
            prevSkillNameItem = comboBoxSkillName.SelectedIndex;

            if (File.Exists(AppInfo.FileBesideExe("lufia2Aux.smc")))
            {
                File.Delete(AppInfo.FileBesideExe("lufia2Aux.smc"));
            }
            selectedRom = this.openFileOpenRom.FileName;
            File.Copy(selectedRom, AppInfo.FileBesideExe("lufia2Aux.smc"));
            this.romData = new Rom(AppInfo.FileBesideExe("lufia2Aux.smc"));
            Reference.Init(this.romData.RawData, this.romData.HasHeader);
            updateCompareLabel();
            this.Text = AppInfo.Title + " - " + selectedRom;
            setFieldsState(true);
            loadItemNames();
            loadEnemyNames();
            loadSpellNames();
            loadSkillNames();
            comboBoxEnemies.SelectedIndex = prevEnemyItem;
            comboBoxSpells.SelectedIndex = prevSpellItem;
            comboBoxSkillName.SelectedIndex = prevSkillNameItem;
            updateEnemyFields(comboBoxEnemies.SelectedIndex);
            loadExtraTabs();
            //comboBoxDrop.SelectedIndex = prevDropItem;
            canSave = true;
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {

        }

        private void setFieldsState(bool value)
        {
            this.comboBoxEnemies.Enabled = value;
            this.numericUpLevel.Enabled = value;
            this.textBoxName.Enabled = value;
            this.numericUpLevel.Enabled = value;
            this.numericUpSprite.Enabled = value;
            this.numericUpPattern.Enabled = value;
            this.numericUpHP.Enabled = value;
            this.numericUpMP.Enabled = value;
            this.numericUpATP.Enabled = value;
            this.numericUpDFP.Enabled = value;
            this.numericUpAGL.Enabled = value;
            this.numericUpINT.Enabled = value;
            this.numericUpGUT.Enabled = value;
            this.numericUpMGR.Enabled = value;
            this.numericUpDownEXP.Enabled = value;
            this.numericUpDownGOLD.Enabled = value;
            this.comboBoxDrop.Enabled = value;
            this.numericUpDownProb.Enabled = value;
        }

        private void updateEnemyFields(int ind)
        {
            
            string offset = this.romData.getEnemyOffset(ind);
            this.labelEnemyID.Text = comboBoxEnemies.SelectedIndex.ToString() + "   ( " + comboBoxEnemies.SelectedIndex.ToString("X2") + " )";
            this.labelEnemyOffset.Text = offset;
            
            this.textBoxName.Text = this.romData.readDataString(offset, 13);
            this.numericUpLevel.Text = this.romData.readDataInt(hexAdd(offset, "D"), 1, false).ToString();
            this.numericUpSprite.Text = this.romData.readDataInt(hexAdd(offset, "F"), 1, false).ToString();
            this.numericUpPattern.Text = this.romData.readDataInt(hexAdd(offset, "10"), 1, false).ToString();
            this.numericUpHP.Text = this.romData.readDataInt(hexAdd(offset, "11"), 2, true).ToString();
            this.numericUpMP.Text = this.romData.readDataInt(hexAdd(offset, "13"), 2, true).ToString();
            this.numericUpATP.Text = this.romData.readDataInt(hexAdd(offset, "15"), 2, true).ToString();
            this.numericUpDFP.Text = this.romData.readDataInt(hexAdd(offset, "17"), 2, true).ToString();
            this.numericUpAGL.Text = (this.romData.readDataInt(hexAdd(offset, "19"), 1, false)).ToString();
            this.numericUpINT.Text = (this.romData.readDataInt(hexAdd(offset, "1A"), 1, false)).ToString();
            this.numericUpGUT.Text = (this.romData.readDataInt(hexAdd(offset, "1B"), 1, false)).ToString();
            this.numericUpMGR.Text = (this.romData.readDataInt(hexAdd(offset, "1C"), 1, false)).ToString();
            this.numericUpDownEXP.Text = this.romData.readDataInt(hexAdd(offset, "1D"), 2, true).ToString();
            this.numericUpDownGOLD.Text = this.romData.readDataInt(hexAdd(offset, "1F"), 2, true).ToString();

            string offset2;
            if (ind < (maxEnemies - 1))
                offset2 = this.romData.getEnemyOffset(ind + 1);
            else
                offset2 = "B5168";
            

            this.textBoxAttScript.Text = this.romData.readAttScript(offset, offset2);
            this.textBoxRctScript.Text = this.romData.readRctScript(offset, offset2);

            if (this.romData.readDataInt(hexAdd(offset, "21"), 1, false) == 3)
            {
                comboBoxDrop.Enabled = true;
                numericUpDownProb.Enabled = true;
                // Drop word = item (9 bits) | chance << 9. The old code showed chance / 2 and
                // wrote it back rounded, which slowly changed odd chances; now the chance is kept exact.
                int v1 = this.romData.readDataInt(hexAdd(offset, "23"), 1, false);
                int v2 = this.romData.readDataInt(hexAdd(offset, "22"), 1, false) + ((v1 & 1) != 0 ? 0x100 : 0);
                numericUpDownProb.Text = (v1 >> 1).ToString();
                //MessageBox.Show(v2.ToString());
                //System.Diagnostics.Debug.WriteLine("LOAD ITEM offset: " + offset + " + 22H = " + hexAdd(offset, "22") + "    23H = " + hexAdd(offset, "23") + "  >>  " + v2.ToString());
                this.comboBoxDrop.SelectedIndex = v2;
            } 
            else 
            {
                this.comboBoxDrop.SelectedIndex = 0;
                this.comboBoxDrop.Enabled = false;
                this.numericUpDownProb.Text = "0";
                this.numericUpDownProb.Enabled = false;
            }
            highlightEnemy(ind);
        }

        public int getItemInd(string enemyOffset)
        {
            int result = -1;
            
            return result;
        }

        private string hexAdd(string value1, string value2)
        {
            int v1 = int.Parse(value1, System.Globalization.NumberStyles.HexNumber);
            int v2 = int.Parse(value2, System.Globalization.NumberStyles.HexNumber);
            return (v1 + v2).ToString("X");
        }

        private void updateEnemyValues(int value)
        {
            if (!canSave || !legacyTabs)
            {
                return;
            }

            string offset = this.romData.getEnemyOffset(value);

            Object[] objs = { this.textBoxName, this.numericUpLevel, this.numericUpSprite, this.numericUpPattern, this.numericUpHP, this.numericUpMP,
                            this.numericUpATP, this.numericUpDFP, this.numericUpAGL, this.numericUpINT, this.numericUpGUT, this.numericUpMGR,
                            this.numericUpDownEXP, this.numericUpDownGOLD};

            foreach (Object obj in objs)
            {
                TextBox _sender = obj as TextBox;
                if (_sender != null)
                {
                    //MessageBox.Show("text: " + _sender.Text);
                    this.romData.writeDataString(_sender.Text, offset, 13);
                }
                else
                {
                    NumericUpDown _sender2 = obj as NumericUpDown;
                    if (_sender2 != null)
                    {
                        string[] onebyte = {"D", "F", "10"};
                        int v;
                        if (Array.Exists(onebyte, el => el == (_sender2.Tag.ToString())))
                        {
                            v = Convert.ToInt32(_sender2.Value);
                            this.romData.writeDataInt(v, hexAdd(offset, _sender2.Tag.ToString()), 1, false);
                        }
                        else 
                        {
                            string[] onebyteWithDiv = { "19", "1A", "1B", "1C" };
                            if (Array.Exists(onebyteWithDiv, el => el == (_sender2.Tag.ToString()))) 
                            {
                                //v = Math.Max(1, Convert.ToInt32(_sender2.Value) / 2);
                                v = Convert.ToInt32(_sender2.Value);
                                this.romData.writeDataInt(v, hexAdd(offset, _sender2.Tag.ToString()), 1, false);
                            }
                            else
                            {
                                v = Convert.ToInt32(_sender2.Value);
                                this.romData.writeDataInt(v, hexAdd(offset, _sender2.Tag.ToString()), 2, true);
                            }
                        }
                    }
                }
            }

            if (comboBoxDrop.Enabled)
            {

                int v1, v2;
                v1 = this.comboBoxDrop.SelectedIndex;
                v2 = Convert.ToInt32(numericUpDownProb.Value) * 2;
                if (v1 > 255)
                {
                    v1 -= 256;
                    v2 += 1;
                }

                this.romData.writeDataInt(v1, hexAdd(offset, "22"), 1, false);
                this.romData.writeDataInt(v2, hexAdd(offset, "23"), 1, false);
                
            } 
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveRom(true);
        }

        /// <summary>Stores every tab, backs up the file on disk and writes the ROM. False if it didn't save.</summary>
        private bool SaveRom(bool announce)
        {
            try
            {
                // commitEverything also stores the enemy skill name box, which this used to miss.
                commitEverything();
                if (!BackupBeforeWriting("")) return false;
                this.romData.SaveData(selectedRom);
                if (announce) MessageBox.Show("Saved!");
                return true;
            }
            catch (Exception x)
            {
                MessageBox.Show("Error saving rom: " + x.ToString());
                return false;
            }
        }

        // ---------------- unsaved changes ----------------

        /// <summary>
        /// Does the ROM in the editor differ from the file on disk? Every tab is stored first, so
        /// edits still sitting in a box count. Comparing the bytes (rather than tracking edits)
        /// means an edit that was changed back doesn't count, and nothing can be missed.
        /// </summary>
        private bool HasUnsavedChanges()
        {
            if (this.romData == null || !canSave || selectedRom == null) return false;
            try { commitEverything(); }
            catch (Exception) { }
            byte[] disk;
            try { disk = File.ReadAllBytes(selectedRom); }
            catch (Exception) { return true; }   // the file is gone or locked: nothing on disk matches
            byte[] cur = this.romData.RawData;
            if (disk.Length != cur.Length) return true;
            for (int i = 0; i < cur.Length; i++) if (cur[i] != disk[i]) return true;
            return false;
        }

        /// <summary>Asks Save / Don't save / Cancel if there are unsaved changes. True = go ahead.</summary>
        private bool ConfirmUnsaved(string beforeWhat)
        {
            if (!HasUnsavedChanges()) return true;
            DialogResult r = MessageBox.Show(
                "You have unsaved changes to " + Path.GetFileName(selectedRom) + ".\n\n" +
                "Save them before " + beforeWhat + "?",
                AppInfo.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1);
            if (r == DialogResult.Cancel) return false;
            if (r == DialogResult.No) return true;
            return SaveRom(false);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmUnsaved("closing")) e.Cancel = true;
        }

        private void comboBoxEnemies_SelectedIndexChanged(object sender, EventArgs e)
        {
            updateEnemyValues(this.prevValueEnemy);
            updateEnemyFields(comboBoxEnemies.SelectedIndex);
            this.prevValueEnemy = comboBoxEnemies.SelectedIndex; // Convert.ToInt32(numericUpID.Value);
        }


        /********************************************************************
         * Spells
        ********************************************************************/

        private void comboBoxSpells_SelectedIndexChanged(object sender, EventArgs e)
        {
            updateSpellValues(this.prevValueSpell);
            updateSpellFields(comboBoxSpells.SelectedIndex);
            this.prevValueSpell = comboBoxSpells.SelectedIndex; // Convert.ToInt32(numericUpID.Value);
        }

        private void updateSpellFields(int ind)
        {

            string offset = this.romData.getSpellOffset(ind);
            this.labelSpellID.Text = comboBoxSpells.SelectedIndex.ToString() + "   ( " + comboBoxSpells.SelectedIndex.ToString("X2") + " )";
            this.labelSpellOffset.Text = offset;

            this.textBoxSpellName.Text = this.romData.readDataString(offset, 8);
            this.numericUpDownMPCost.Text = this.romData.readDataInt(hexAdd(offset, "C"), 2, true).ToString();
            this.numericUpDownGoldCost.Text = this.romData.readDataInt(hexAdd(offset, "F"), 2, true).ToString();

            
            string offset2;
            if (ind < (maxSpells - 1))
                offset2 = this.romData.getSpellOffset(ind + 1);
            else
                offset2 = "B0115";
            this.textBoxSpellBehavior.Text = this.romData.readSpellBehavior(hexAdd(offset, "13"), offset2);
            highlightSpell(ind);
        }


        private void updateSpellValues(int value)
        {   
            if (!canSave || !legacyTabs)
            {
                return;
            }
            string offset = this.romData.getSpellOffset(value);
            //System.Diagnostics.Debug.WriteLine("   OK!  " + offset);
            Object[] objs = { this.textBoxSpellName, this.numericUpDownMPCost, this.numericUpDownGoldCost};

            foreach (Object obj in objs)
            {
                TextBox _sender = obj as TextBox;
                if (_sender != null)
                {
                    this.romData.writeDataString(_sender.Text, offset, 8);
                }
                else
                {
                    NumericUpDown _sender2 = obj as NumericUpDown;
                    if (_sender2 != null)
                    {
                        int v = Convert.ToInt32(_sender2.Value);
                        this.romData.writeDataInt(v, hexAdd(offset, _sender2.Tag.ToString()), 2, true);
                    }
                }
            }
        }

        private void numericUpStat_ValueChanged(object sender, EventArgs e)
        {
            this.labelRealAGL.Text = (this.numericUpAGL.Value * 2).ToString();
            this.labelRealINT.Text = (this.numericUpINT.Value * 2).ToString();
            this.labelRealGUT.Text = (this.numericUpGUT.Value * 2).ToString();
            this.labelRealMGR.Text = (this.numericUpMGR.Value * 2).ToString();
        }



        /********************************************************************
         * Skills
        ********************************************************************/

        private void comboBoxSkills_SelectedIndexChanged(object sender, EventArgs e)
        {
            updateSkillValues(this.prevValueSkill);
            updateSkillFields(comboBoxSkillName.SelectedIndex);
            this.prevValueSkill = comboBoxSkillName.SelectedIndex; // Convert.ToInt32(numericUpID.Value);
        }

        private void updateSkillFields(int ind)
        {

            string offset = this.romData.getSkillOffset(ind);
            int len = this.romData.skillNamesLength[ind];
            this.labelSkillID.Text = (comboBoxSkillName.SelectedIndex + 1).ToString() + "   ( " + (comboBoxSkillName.SelectedIndex + 1).ToString("X2") + " )";
            this.labelSkillOffset.Text = offset;
            
            this.textBoxSkillName.Text = this.romData.readDataString(offset, len);
            this.textBoxSkillName.MaxLength = len;
            highlightSkill(ind);

            

        }


        private void updateSkillValues(int value)
        {
            if (!canSave || !legacyTabs)
            {
                return;
            }
            string offset = this.romData.getSkillOffset(value);
            //System.Diagnostics.Debug.WriteLine("   OK!  " + offset);
            Object[] objs = { this.textBoxSkillName};

            foreach (Object obj in objs)
            {
                TextBox _sender = obj as TextBox;
                if (_sender != null)
                {
                    // Safety checks: only write when the name really changed, and only into a
                    // real name slot (type byte E0 before it, 00 right after its last letter).
                    if (value < 0 || value >= this.romData.skillNamesLength.Length) return;
                    int len = this.romData.skillNamesLength[value];
                    int o = int.Parse(offset, System.Globalization.NumberStyles.HexNumber);
                    byte[] data = this.romData.RawData;
                    if (_sender.Text.Trim() == this.romData.readDataString(offset, len).Trim()) return;
                    if (o < 1 || o + len >= data.Length || data[o - 1] != 0xE0 || data[o + len] != 0)
                    {
                        MessageBox.Show("The name slot for this skill doesn't look right, so the new name was not saved.\n" +
                            "(Try File > Repair enemy skill names from clean ROM.)", "Enemy Skills",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    this.romData.writeDataString(_sender.Text, offset, len);
                }
                else
                {
                    // no numbers here!
                }
            }
        }
    }
}

public class ComboboxItem
{
    public string Text { get; set; }
    public object Value { get; set; }

    public override string ToString()
    {
        return Text;
    }
}