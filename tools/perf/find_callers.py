import struct,sys
EXE=r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar\Celtic kings.exe"
d=open(EXE,'rb').read()
e=struct.unpack_from("<I",d,0x3C)[0]; coff=e+4
n=struct.unpack_from("<H",d,coff+2)[0]; optsz=struct.unpack_from("<H",d,coff+16)[0]
opt=coff+20; base=struct.unpack_from("<I",d,opt+28)[0]; first=opt+optsz
secs=[]
for i in range(n):
    o=first+i*40
    nm=d[o:o+8].rstrip(b"\0").decode('latin-1')
    vs,va,rs,ro=struct.unpack_from("<IIII",d,o+8)
    secs.append((nm,base+va,vs,ro,rs))
tgt=int(sys.argv[1],16)
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
md=Cs(CS_ARCH_X86,CS_MODE_32)
hits=[]
for nm,va,vs,ro,rs in secs:
    if nm!='.text': continue
    blob=d[ro:ro+rs]
    for i in range(len(blob)-5):
        if blob[i]==0xE8:
            rel=struct.unpack_from("<i",blob,i+1)[0]
            if va+i+5+rel==tgt: hits.append(va+i)
print("calls to %08X: %d"%(tgt,len(hits)))
# classify: look at the 16 bytes after the call
for h in hits:
    o=None
    for nm,va,vs,ro,rs in secs:
        if va<=h<va+vs: o=ro+(h-va)
    ins=list(md.disasm(d[o:o+20],h))
    txt=" ; ".join("%s %s"%(x.mnemonic,x.op_str) for x in ins[:5])
    print("  %08X  %s"%(h,txt))
