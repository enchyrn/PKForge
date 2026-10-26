import struct, sys
ORDERS = ["GAEM","GAME","GEAM","GEMA","GMAE","GMEA","AGEM","AGME","AEGM","AEMG","AMGE","AMEG",
          "EGAM","EGMA","EAGM","EAMG","EMGA","EMAG","MGAE","MGEA","MAGE","MAEG","MEGA","MEAG"]
SIZES = {0: 0xF2C, 1: 0xF80, 2: 0xF80, 3: 0xF80, 4: 0xF08, 13: 0x7D0}
src, dst, want_pid, add, expect_exp = sys.argv[1], sys.argv[2], int(sys.argv[3], 16), int(sys.argv[4]), int(sys.argv[5])
data = bytearray(open(src, "rb").read())
def sector_checksum(sec, sid):
    n = SIZES.get(sid, 0xF80); s = sum(struct.unpack_from(f"<{n//4}I", sec, 0)) & 0xFFFFFFFF
    return ((s >> 16) + (s & 0xFFFF)) & 0xFFFF
# newest valid slot's section 1
best = None
for slot in (0, 1):
    for i in range(14):
        o = slot*0xE000 + i*0x1000
        sid, chk, sig, idx = struct.unpack_from("<HHII", data, o+0xFF4)
        if sig == 0x08012025 and sid == 1 and (best is None or idx > best[1]): best = (o, idx)
o, idx = best
sec = data[o:o+0x1000]
assert sector_checksum(sec, 1) == struct.unpack_from("<H", sec, 0xFF6)[0], "section 1 checksum already bad"
count = struct.unpack_from("<I", sec, 0x234)[0]
done = False
for n in range(count):
    m = 0x238 + n*100
    pid, otid = struct.unpack_from("<II", sec, m)
    if pid != want_pid: continue
    key = pid ^ otid
    dec = bytearray(b"".join(struct.pack("<I", struct.unpack_from("<I", sec, m+0x20+i)[0] ^ key) for i in range(0, 48, 4)))
    assert sum(struct.unpack_from("<24H", dec, 0)) & 0xFFFF == struct.unpack_from("<H", sec, m+0x1C)[0], "mon checksum already bad"
    g = ORDERS[pid % 24].index("G") * 12
    exp = struct.unpack_from("<I", dec, g+4)[0]
    assert exp == expect_exp, f"EXP is {exp}, expected {expect_exp}"
    struct.pack_into("<I", dec, g+4, exp + add)
    struct.pack_into("<H", sec, m+0x1C, sum(struct.unpack_from("<24H", dec, 0)) & 0xFFFF)
    for i in range(0, 48, 4): struct.pack_into("<I", sec, m+0x20+i, struct.unpack_from("<I", dec, i)[0] ^ key)
    print(f"party slot {n+1}: EXP {exp} -> {exp+add} (save index {idx})"); done = True
assert done, "Pokémon not found in the party"
struct.pack_into("<H", sec, 0xFF6, sector_checksum(sec, 1))
data[o:o+0x1000] = sec
open(dst, "wb").write(data)
