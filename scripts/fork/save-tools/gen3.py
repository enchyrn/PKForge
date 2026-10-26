import struct, sys, os
ORDERS = ["GAEM","GAME","GEAM","GEMA","GMAE","GMEA","AGEM","AGME","AEGM","AEMG","AMGE","AMEG",
          "EGAM","EGMA","EAGM","EAMG","EMGA","EMAG","MGAE","MGEA","MAGE","MAEG","MEGA","MEAG"]
def text(b):
    out = ""
    for c in b:
        if c == 0xFF: break
        if 0xBB <= c <= 0xD4: out += chr(ord('A') + c - 0xBB)
        elif 0xD5 <= c <= 0xEE: out += chr(ord('a') + c - 0xD5)
        elif 0xA1 <= c <= 0xAA: out += chr(ord('0') + c - 0xA1)
        elif c == 0x00: out += " "
        else: out += "?"
    return out
SPECIES = {277: "Treecko", 288: "Zigzagoon", 286: "Poochyena", 290: "Wurmple", 280: "Torchic", 283: "Mudkip", 304: "Taillow", 309: "Ralts", 392: "Seedot", 302: "Lotad"}
MAPS = {(0,9): "Littleroot Town", (0,10): "Oldale Town", (0,16): "Route 101", (0,17): "Route 102", (0,18): "Route 103", (0,19): "Route 104", (0,2): "Petalburg City"}
def mon(b):
    pid, otid = struct.unpack_from("<II", b, 0)
    if pid == 0 and otid == 0: return None
    key = pid ^ otid
    enc = b[0x20:0x50]
    dec = b"".join(struct.pack("<I", struct.unpack_from("<I", enc, i)[0] ^ key) for i in range(0, 48, 4))
    order = ORDERS[pid % 24]
    g = dec[order.index("G")*12: order.index("G")*12+12]
    species, item, exp = struct.unpack_from("<HHI", g, 0)
    friendship = g[9]
    level = b[0x54] if len(b) >= 0x58 else None
    hp, maxhp = struct.unpack_from("<HH", b, 0x56) if len(b) >= 0x5A else (None, None)
    return dict(nick=text(b[8:18]), species=SPECIES.get(species, species), exp=exp, level=level, hp=hp, maxhp=maxhp, friendship=friendship, pid=f"{pid:08x}")
def sectors(data):
    best = {}
    for slot in (0, 1):
        secs = {}
        idx = None
        for i in range(14):
            off = slot*0xE000 + i*0x1000
            sid, chk, sig, sidx = struct.unpack_from("<HHII", data, off+0xFF4)
            if sig != 0x08012025: break
            secs[sid] = data[off:off+0xFF4]; idx = sidx
        if len(secs) == 14: best[slot] = (idx, secs)
    slot = max(best, key=lambda s: best[s][0])
    return best[slot]
def describe_save(data, label):
    idx, secs = sectors(data)
    sb2, sb1 = secs[0], secs[1]
    name = text(sb2[0:7]); h, m, s = struct.unpack_from("<HBB", sb2, 0x0E)
    x, y = struct.unpack_from("<hh", sb1, 0); grp, num = sb1[4], sb1[5]
    count = struct.unpack_from("<I", sb1, 0x234)[0]
    party = [mon(sb1[0x238+i*100:0x238+(i+1)*100]) for i in range(min(count, 6))]
    print(f"{label}: saveIndex={idx} trainer={name} playtime={h}:{m:02d}:{s:02d} map={MAPS.get((grp,num),(grp,num))} pos=({x},{y})")
    for p in party: print(f"    {p}")
def describe_state(path, label):
    st = open(path, "rb").read()
    # live RAM: EWRAM at 0x21000, IWRAM at 0x19000
    ew = st[0x21000:0x61000]; iw = st[0x19000:0x21000]
    sb1p, sb2p = struct.unpack_from("<II", iw, 0x5D8C)
    sb1 = ew[sb1p-0x02000000:]; sb2 = ew[sb2p-0x02000000:]
    h, m, s = struct.unpack_from("<HBB", sb2, 0x0E)
    x, y = struct.unpack_from("<hh", sb1, 0); grp, num = sb1[4], sb1[5]
    count = ew[0x244E9]
    party = [mon(ew[0x244EC+i*100:0x244EC+(i+1)*100]) for i in range(min(count, 6))]
    print(f"{label} (live RAM): trainer={text(sb2[0:7])} playtime={h}:{m:02d}:{s:02d} map={MAPS.get((grp,num),(grp,num))} pos=({x},{y})")
    for p in party: print(f"    {p}")
    # embedded save data (extdata after 0x61000)
    off = 0x61000
    while off + 16 <= len(st):
        tag, size, doff = struct.unpack_from("<IiQ", st, off); off += 16
        if tag == 0: break
        if tag == 2: describe_save(st[doff:doff+size], f"{label} (its save copy)")
for p in sys.argv[1:]:
    if os.path.basename(p).startswith("resume"): describe_state(p, os.path.basename(p))
    else: describe_save(open(p, "rb").read(), os.path.basename(p))
