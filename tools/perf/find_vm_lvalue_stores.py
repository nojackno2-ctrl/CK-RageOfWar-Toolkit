import struct
from capstone import Cs,CS_ARCH_X86,CS_MODE_32
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
def off(va):
    for nm,va0,vs,ro,rs in secs:
        if va0<=va<va0+vs:
            dd=va-va0
            return ro+dd if dd<rs else None
md=Cs(CS_ARCH_X86,CS_MODE_32)
TGT=0x481A20
res=[]
for nm,va,vs,ro,rs in secs:
    if nm!='.text': continue
    blob=d[ro:ro+rs]
    for i in range(len(blob)-5):
        if blob[i]==0xE8:
            rel=struct.unpack_from("<i",blob,i+1)[0]
            if va+i+5+rel!=TGT: continue
            site=va+i
            ins=list(md.disasm(blob[i+5:i+5+40], site+5))
            # find test/cmp + je within first 4 instrs
            jt=None; reg=None
            for k,x in enumerate(ins[:4]):
                if x.mnemonic=='test' and x.op_str.split(',')[0].strip()==x.op_str.split(',')[1].strip():
                    reg=x.op_str.split(',')[0].strip()
                elif x.mnemonic=='je' and reg:
                    jt=int(x.op_str,16); ktaken=k; break
            if jt is None: continue
            to=off(jt)
            tins=list(md.disasm(d[to:to+16], jt))
            if not tins or tins[0].mnemonic!='xor': continue
            if tins[0].op_str.split(',')[0].strip()!=tins[0].op_str.split(',')[1].strip(): continue
            z=tins[0].op_str.split(',')[0].strip()
            # next instr on the null path must store through z
            st=tins[1] if len(tins)>1 else None
            if st is None: continue
            if not (st.mnemonic in ('mov','rep movsd') and ('[%s]'%z) in st.op_str.split(',')[0]): continue
            res.append((site,jt,z,st.address,"%s %s"%(st.mnemonic,st.op_str)))
print("VM-lvalue store sites (resolve -> null path stores through zeroed reg): %d"%len(res))
for r in sorted(set(res)):
    print("  call@%08X  nullpath@%08X (%s)  store@%08X  %s"%r)
