@echo off
setlocal
rem Builds "Dual Blade Editor.exe" one folder up, next to the offset .txt files.
rem Uses the C# compiler that ships with the .NET Framework 4 built into Windows.

set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find csc.exe. Install or enable .NET Framework 4.x and try again.
  pause
  exit /b 1
)

cd /d "%~dp0"
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ ^
  /out:"..\Dual Blade Editor.exe" ^
  /r:System.dll /r:System.Core.dll /r:System.Data.dll /r:System.Drawing.dll ^
  /r:System.Windows.Forms.dll /r:System.Xml.dll ^
  AppInfo.cs Program.cs Form1.cs Form1.Designer.cs Parser.cs Rom.cs Utils.cs ^
  CapsuleMonsterData.cs CapsuleMonsterTab.cs CapsuleSkillData.cs CapsuleAITab.cs FeedingData.cs CapsuleFeedingTab.cs ^
  ItemData.cs IPData.cs ShopData.cs ShopsTab.cs CaveLootData.cs CaveLootTab.cs UiHelpers.cs EquipmentTab.cs ItemsTab.cs IPTab.cs ^
  ScriptTools.cs StatusEffectsPanel.cs EnemyStatusTab.cs ^
  EffectEditorPanel.cs SpellEffectsTab.cs ChangeTracking.cs PatchTools.cs ChangeReport.cs BackupTools.cs ^
  GrowthChart.cs CharacterData.cs CharactersTab.cs PartyData.cs PartyTab.cs EquipElements.cs EnemyElements.cs EnemyData.cs EnemyGridTab.cs RomCheck.cs DescriptionData.cs DescriptionsTab.cs RomExpansion.cs EncounterData.cs EncountersTab.cs DialogueData.cs DialogueTab.cs GameFixes.cs GameFixesTab.cs ChestData.cs ChestsTab.cs

if errorlevel 1 (
  echo.
  echo Build FAILED - copy the errors above and send them to Claude.
) else (
  echo.
  echo Build OK: "Dual Blade Editor.exe" is in the folder above, next to the offset .txt files.
)
pause
