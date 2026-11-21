-- chest-diff.lua (automatic version): prints every byte of WRAM that changes while you open a chest.
-- No keys needed. Stand next to the chest, run this, wait for "Ready", open the chest,
-- close the message, then copy the console output.
-- Life potion is item 0x11; in the inventory it would be the word 0211 (bytes 11 02).

local SIZE = 0x20000
local LEARN_SAMPLES = 30     -- samples taken while you stand still
local EVERY = 4              -- frames between samples (full copies are slow)
local MAX_LINES = 400

local function snapshot()
  local t = memory.readbyterange(0, SIZE, "WRAM")
  local s = {}
  local base = (t[0] ~= nil) and 0 or 1   -- older versions index from 1
  for i = 0, SIZE - 1 do s[i] = t[i + base] or 0 end
  return s
end

local function name(i)
  return string.format("$%02X:%04X", 0x7E + math.floor(i / 0x10000), i % 0x10000)
end

print("Learning which bytes change on their own - don't move until it says Ready...")
local noisy = {}
local prev = snapshot()
local frames, samples, printed = 0, 0, 0
local ready = false
local lastSum = nil

event.onframeend(function()
  frames = frames + 1
  if frames % EVERY ~= 0 then return end
  local cur = snapshot()
  samples = samples + 1

  if not ready then
    for i = 0, SIZE - 1 do if cur[i] ~= prev[i] then noisy[i] = true end end
    prev = cur
    if samples >= LEARN_SAMPLES then
      local n = 0
      for _ in pairs(noisy) do n = n + 1 end
      print(n .. " bytes change on their own and will be ignored.")
      print("Ready (frame " .. emu.framecount() .. "). Open the chest now.")
      ready = true
    end
    return
  end

  local changes = {}
  for i = 0, SIZE - 1 do
    if cur[i] ~= prev[i] and not noisy[i] then
      local mark = ""
      if cur[i] == 0x11 then mark = "   <- 11 (Life potion?)" end
      if i > 0 and cur[i] == 0x02 and cur[i - 1] == 0x11 then mark = "   <- 02 after 11 (Life potion x1?)" end
      changes[#changes + 1] = string.format("  %s: %02X -> %02X%s", name(i), prev[i], cur[i], mark)
    end
  end
  if #changes > 0 and printed < MAX_LINES then
    print("Frame " .. emu.framecount() .. ": " .. #changes .. " bytes changed")
    for k = 1, #changes do
      if printed >= MAX_LINES then print("  (output limit reached)"); break end
      print(changes[k]); printed = printed + 1
    end
  end
  prev = cur
end, "chest diff")
