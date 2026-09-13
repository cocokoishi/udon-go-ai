import gzip, struct, tempfile, unittest
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from katago_model_parser import parse_model, ParseError

def binfloats(vals):
    return b" @BIN@" + b"".join(struct.pack("<f",float(x)) for x in vals) + b" "
def conv(name,y,x,ic,oc):
    n=y*x*ic*oc
    return f"{name} {y} {x} {ic} {oc} 1 1 ".encode()+binfloats(range(n))
def mm(name,ic,oc):
    return f"{name} {ic} {oc} ".encode()+binfloats(range(ic*oc))
def bias(name,c):
    return f"{name} {c} ".encode()+binfloats(range(c))
def bn(name,c):
    return f"{name} {c} 0.001 0 0 ".encode()+binfloats([0]*c)+binfloats([1]*c)
def ordinary(name,c,mid):
    return b"ordinary_block "+name.encode()+b" "+bn(name+".pre",c)+name.encode()+b".a "+conv(name+".r",1,1,c,mid)+bn(name+".mid",mid)+name.encode()+b".b "+conv(name+".f",1,1,mid,c)
def model_bytes(extra=b"", nan=False, version=8):
    c=2; mid=2
    b=b"synthetic "+str(version).encode()+b" 2 1 "
    b+=b"trunk 1 2 2 2 1 1 "
    b+=conv("init",1,1,2,2)+mm("global",1,2)+ordinary("b0",c,mid)+bn("tip",2)+b"tipact "
    b+=b"policy "+conv("p1",1,1,2,1)+conv("g1",1,1,2,1)+bn("g1bn",1)+b"ga "+mm("gbias",3,1)+bn("p1bn",1)+b"pa "+conv("p2",1,1,1,1)+mm("pass",3,1)
    b+=b"value "+conv("v1",1,1,2,1)+bn("v1bn",1)+b"va "+mm("v2",3,2)+bias("v2b",2)+b"v2a "+mm("v3",2,3)+bias("v3b",3)+mm("sv",2,4)+bias("svb",4)+conv("own",1,1,1,1)
    if nan:
        i=b.rfind(struct.pack("<f",0.0)); b=b[:i]+struct.pack("<f",float("nan"))+b[i+4:]
    return b+extra

class ParserTests(unittest.TestCase):
    def write(self,payload):
        td=tempfile.TemporaryDirectory(); p=Path(td.name)/"m.bin.gz"; p.write_bytes(gzip.compress(payload)); return td,p
    def test_complete_model_consumed(self):
        td,p=self.write(model_bytes())
        with td: r=parse_model(p)
        self.assertEqual(r["bytes_consumed"],r["decompressed_size_bytes"])
        self.assertGreater(r["tensor_count"],10)
        self.assertEqual(r["architecture"]["model_version"],8)
    def test_rejects_trailing_data(self):
        td,p=self.write(model_bytes(b"JUNK"))
        with td, self.assertRaisesRegex(ParseError,"trailing unparsed"): parse_model(p)
    def test_rejects_nonfinite(self):
        td,p=self.write(model_bytes(nan=True))
        with td, self.assertRaisesRegex(ParseError,"non-finite"): parse_model(p)
    def test_rejects_wrong_version(self):
        td,p=self.write(model_bytes(version=9))
        with td, self.assertRaisesRegex(ParseError,"requires model version 8"): parse_model(p)

if __name__=="__main__": unittest.main()
