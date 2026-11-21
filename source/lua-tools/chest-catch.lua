-- chest-catch.lua: when a chest gives an item, logs which code wrote it and saves the game's
-- working memory to a file next to this script, so the chest's event script can be studied.
-- Stand next to an unopened chest, run this, open the chest, then send back the console output
-- and the file chest-wram-<item>.bin.

print("chest-catch: starting")
local GIVE = 0x0A06          -- item being given (found with chest-diff.lua)
local hits = 0
local saved = false

local function hex(v, n) return string.format("%0" .. (n or 2) .. "X", v or 0) end
local function w8(a) return memory.read_u8(a, "WRAM") end
local function w16(a) return memory.read_u16_le(a, "WRAM") end

-- the folder this script is in
local here = ""
pcall(function()
  here = (debug.getinfo(1, "S").source or ""):match("^@(.*[\\/])") or ""
end)

local function saveWram(tag)
  local t = memory.readbyterange(0, 0x20000, "WRAM")
  local base = (t[0] ~= nil) and 0 or 1
  local parts = {}
  for i = 0, 0x20000 - 1, 4096 do
    local chunk = {}
    for k = 0, 4095 do chunk[#chunk + 1] = string.char(t[i + k + base] or 0) end
    parts[#parts + 1] = table.concat(chunk)
  end
  local path = here .. "chest-wram-" .. tag .. ".bin"
  local ok, err = pcall(function()
    local f, e = io.open(path, "wb")
    if not f then error(e) end
    f:write(table.concat(parts))
    f:close()
  end)
  if ok then console.log("   Saved working memory to: " .. path)
  else console.log("   Could not save the file (" .. tostring(err) .. ") - printing the script area instead.") end
  return ok
end

-- Fallback: print $7E:E000-$7E:E7FF (where the chest's script was running) as hex lines.
local function printScriptArea()
  console.log("   Script area $7E:E000-$7E:E7FF:")
  for row = 0xE000, 0xE7F0, 16 do
    local b = {}
    for k = 0, 15 do b[#b + 1] = hex(w8(row + k)) end
    console.log("   " .. hex(row, 4) .. ": " .. table.concat(b, " "))
  end
end

local function onWrite(addr, val)
  hits = hits + 1
  if hits > 12 then return end
  local item = w8(GIVE)
  console.log("#" .. hits .. "  frame " .. emu.framecount() .. "  write to " .. hex(addr or 0, 6) ..
              "  $0A06 now " .. hex(item) .. "  $0A07 " .. hex(w8(0x0A07)))
  local ok, regs = pcall(emu.getregisters)
  if ok then
    local p = {}
    for k, v in pairs(regs) do p[#p + 1] = k .. "=" .. hex(v, 4) end
    table.sort(p)
    console.log("   regs: " .. table.concat(p, " "))
  end
  local ptr, bank = w16(0x09B7), w8(0x09B9)
  console.log("   script pointer $" .. hex(bank) .. ":" .. hex(ptr, 4) .. "   map $05AC = " .. hex(w16(0x05AC), 4))
  if bank == 0x7E or bank == 0x7F then
    local start = (bank - 0x7E) * 0x10000 + ptr
    local b = {}
    for i = -24, 23 do
      local a = start + i
      b[#b + 1] = (a >= 0 and a < 0x20000) and hex(w8(a)) or "??"
    end
    console.log("   script bytes from -24: " .. table.concat(b, " "))
  end
  if not saved and item ~= 0 then
    saved = true
    local ok, r = pcall(saveWram, hex(item))
    if not (ok and r) then printScriptArea() end
  end
end

-- Low WRAM can be reached as $7E:0A06 or through any bank $00-$3F / $80-$BF, and the watch
-- only fires for the exact address used, so watch them all.
local bases = { 0x7E0000 }
for b = 0x00, 0x3F do bases[#bases + 1] = b * 0x10000 end
for b = 0x80, 0xBF do bases[#bases + 1] = b * 0x10000 end
for _, base in ipairs(bases) do
  event.onmemorywrite(function(addr, val)
    local ok, err = pcall(onWrite, addr, val)
    if not ok then console.log("   (error: " .. tostring(err) .. ")") end
  end, base + GIVE, "give@" .. hex(base, 6))
end
-- Backup trigger: if $0A06 changes and the write watch didn't catch it, still save/print.
local lastItem = w8(GIVE)
event.onframeend(function()
  local v = w8(GIVE)
  if v ~= lastItem then
    console.log("(frame check) $0A06 changed " .. hex(lastItem) .. " -> " .. hex(v) ..
                " at frame " .. emu.framecount() .. (hits == 0 and " - the write watch did NOT fire" or ""))
    lastItem = v
    if not saved and v ~= 0 then
      saved = true
      console.log("   script pointer $" .. hex(w8(0x09B9)) .. ":" .. hex(w16(0x09B7), 4) ..
                  "   map $05AC = " .. hex(w16(0x05AC), 4))
      local ok, r = pcall(saveWram, hex(v))
      if not (ok and r) then printScriptArea() end
    end
  end
end, "give frame check")

console.log("Watching $0A06. Open the chest now.")
