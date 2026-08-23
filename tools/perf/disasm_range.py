import struct, sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32
EXE=r"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar\Celtic kings.exe"
data=open(EXE,'rb').read()
def sections(d):
    e=struct.unpack_from("<I",d,0x3C)[0]; coff=e+4
    n=struct.unpack_from("<H",d,coff+2)[0]; optsz=struct.unpack_from("<H",d,coff+16)[0]
    opt=coff+20; base=struct.unpack_from("<I",d,opt+28)[0]; first=opt+optsz
    out=[]
    for i in range(n):
        o=first+i*40
        nm=d[o:o+8].rstrip(b"\0").decode('latin-1')
        vs,va,rs,ro=struct.unpack_from("<IIII",d,o+8)
        out.append((nm,base+va,vs,ro,rs))
    return out
S=sections(data)
def off(va):
    for nm,va0,vs,ro,rs in S:
        if va0<=va<va0+vs:
            d=va-va0
            return ro+d if d<rs else None
    return None
md=Cs(CS_ARCH_X86,CS_MODE_32); md.detail=True
def dis(start,end,mark=()):
    o=off(start)
    for i in md.disasm(data[o:o+(end-start)],start):
        m='  <<< ' if i.address in mark else '      '
        print("%08X  %-24s %s %s%s"%(i.address," ".join("%02x"%b for b in i.bytes),i.mnemonic,i.op_str,m))
if __name__=="__main__":
    a=int(sys.argv[1],16); b=int(sys.argv[2],16)
    mark=set(int(x,16) for x in sys.argv[3:])
    dis(a,b,mark)
