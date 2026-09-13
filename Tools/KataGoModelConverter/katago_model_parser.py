#!/usr/bin/env python3
from __future__ import annotations
import argparse, gzip, hashlib, json, math, struct
from dataclasses import dataclass, asdict
from pathlib import Path

MARKER=b"@BIN@"

class ParseError(ValueError):
    pass

@dataclass
class TensorInfo:
    name:str
    kind:str
    shape:list[int]
    element_count:int
    byte_count:int
    data_sha256:str
    decompressed_offset:int

class Reader:
    def __init__(self,data:bytes):
        self.data=data; self.p=0; self.tensors=[]
    def skip_ws(self):
        d=self.data; n=len(d); p=self.p
        while p<n and d[p] in b" \t\r\n": p+=1
        self.p=p
    def token(self)->str:
        self.skip_ws()
        if self.p>=len(self.data): raise ParseError("unexpected EOF while reading token")
        s=self.p
        while self.p<len(self.data) and self.data[self.p] not in b" \t\r\n":
            self.p+=1
        try: return self.data[s:self.p].decode("ascii")
        except UnicodeDecodeError as e: raise ParseError(f"non-ASCII token at {s}") from e
    def integer(self)->int:
        t=self.token()
        try:return int(t)
        except ValueError as e: raise ParseError(f"expected integer, got {t!r}") from e
    def floating(self)->float:
        t=self.token()
        try:x=float(t)
        except ValueError as e: raise ParseError(f"expected float, got {t!r}") from e
        if not math.isfinite(x): raise ParseError(f"non-finite scalar {t}")
        return x
    def binary(self,name:str,kind:str,shape:list[int]):
        count=1
        for x in shape:
            if x<=0: raise ParseError(f"{name}: invalid shape {shape}")
            count*=x
        skipped=0
        while self.p<len(self.data) and self.data[self.p] in b" \t\r\n":
            self.p+=1; skipped+=1
            if skipped>100: raise ParseError(f"{name}: excessive whitespace before @BIN@")
        if self.data[self.p:self.p+5]!=MARKER:
            raise ParseError(f"{name}: missing @BIN@ at offset {self.p}")
        self.p+=5
        start=self.p; nbytes=count*4; end=start+nbytes
        if end>len(self.data): raise ParseError(f"{name}: truncated tensor")
        raw=self.data[start:end]
        for (x,) in struct.iter_unpack("<f", raw):
            if not math.isfinite(x): raise ParseError(f"{name}: non-finite tensor value")
        self.p=end
        self.tensors.append(TensorInfo(name,kind,shape,count,nbytes,hashlib.sha256(raw).hexdigest(),start))

def expect(cond,msg):
    if not cond: raise ParseError(msg)

def parse_conv(r:Reader):
    name=r.token(); y=r.integer(); x=r.integer(); ic=r.integer(); oc=r.integer(); dy=r.integer(); dx=r.integer()
    expect(y>0 and x>0 and ic>0 and oc>0 and dy>0 and dx>0,f"{name}: invalid conv descriptor")
    expect(y%2==1 and x%2==1,f"{name}: even convolution size unsupported")
    r.binary(name,"conv",[y,x,ic,oc])
    return {"name":name,"y":y,"x":x,"in":ic,"out":oc,"dy":dy,"dx":dx}

def parse_matmul(r:Reader):
    name=r.token(); ic=r.integer(); oc=r.integer()
    expect(ic>0 and oc>0,f"{name}: invalid matmul descriptor")
    r.binary(name,"matmul",[ic,oc])
    return {"name":name,"in":ic,"out":oc}

def parse_bias(r:Reader):
    name=r.token(); c=r.integer()
    expect(c>0,f"{name}: invalid bias channels")
    r.binary(name,"bias",[c]); return {"name":name,"channels":c}

def parse_bn(r:Reader):
    name=r.token(); c=r.integer(); eps=r.floating(); hs=r.integer(); hb=r.integer()
    expect(c>0 and eps>0 and hs in (0,1) and hb in (0,1),f"{name}: invalid batchnorm descriptor")
    r.binary(name+".mean","bn_mean",[c]); r.binary(name+".variance","bn_variance",[c])
    if hs: r.binary(name+".scale","bn_scale",[c])
    if hb: r.binary(name+".bias","bn_bias",[c])
    return {"name":name,"channels":c,"epsilon":eps,"has_scale":bool(hs),"has_bias":bool(hb)}

def parse_activation_v8(r:Reader):
    return {"name":r.token(),"kind":"RELU"}

def parse_residual(r:Reader,trunk_c:int):
    name=r.token()
    pre=parse_bn(r); parse_activation_v8(r); reg=parse_conv(r); mid=parse_bn(r); parse_activation_v8(r); final=parse_conv(r)
    expect(pre["channels"]==reg["in"],f"{name}: preBN/regularConv mismatch")
    expect(mid["channels"]==reg["out"]==final["in"],f"{name}: mid channel mismatch")
    expect(pre["channels"]==trunk_c and final["out"]==trunk_c,f"{name}: trunk channel mismatch")
    return {"kind":"ordinary_block","name":name}

def parse_gpool(r:Reader,trunk_c:int):
    name=r.token()
    pre=parse_bn(r); parse_activation_v8(r); reg=parse_conv(r); gp=parse_conv(r); gpbn=parse_bn(r); parse_activation_v8(r); mul=parse_matmul(r); mid=parse_bn(r); parse_activation_v8(r); final=parse_conv(r)
    expect(pre["channels"]==reg["in"]==gp["in"]==trunk_c,f"{name}: pre/gpool/trunk mismatch")
    expect(gpbn["channels"]==gp["out"],f"{name}: gpool BN mismatch")
    expect(gpbn["channels"]*3==mul["in"],f"{name}: gpool pooled width mismatch")
    expect(mid["channels"]==reg["out"]==mul["out"]==final["in"],f"{name}: mid mismatch")
    expect(final["out"]==trunk_c,f"{name}: final trunk mismatch")
    return {"kind":"gpool_block","name":name}

def parse_trunk_v8(r:Reader,num_input:int,num_global:int):
    name=r.token(); nb=r.integer(); tc=r.integer(); mid=r.integer(); regular=r.integer(); dilated=r.integer(); gp=r.integer()
    expect(nb>0 and min(tc,mid,regular,gp)>0,f"{name}: invalid trunk sizes")
    initial=parse_conv(r); initial_mm=parse_matmul(r)
    expect(initial["in"]==num_input and initial["out"]==tc,f"{name}: initial conv mismatch")
    expect(initial_mm["in"]==num_global and initial_mm["out"]==tc,f"{name}: initial global matmul mismatch")
    blocks=[]
    for _ in range(nb):
        kind=r.token()
        if kind=="ordinary_block": blocks.append(parse_residual(r,tc))
        elif kind=="gpool_block": blocks.append(parse_gpool(r,tc))
        else: raise ParseError(f"{name}: unsupported v8 block kind {kind!r}")
    tip=parse_bn(r); parse_activation_v8(r)
    expect(tip["channels"]==tc,f"{name}: trunk tip mismatch")
    return {"name":name,"num_blocks":nb,"trunk_channels":tc,"mid_channels":mid,"regular_channels":regular,
            "legacy_dilated_channels":dilated,"gpool_channels":gp,"blocks":blocks}

def parse_policy_v8(r:Reader,trunk_c:int):
    name=r.token()
    p1=parse_conv(r); g1=parse_conv(r); g1bn=parse_bn(r); parse_activation_v8(r); biasmul=parse_matmul(r); p1bn=parse_bn(r); parse_activation_v8(r); p2=parse_conv(r); passmul=parse_matmul(r)
    expect(p1["in"]==trunk_c and g1["in"]==trunk_c,f"{name}: trunk input mismatch")
    expect(p1["out"]==p1bn["channels"] and g1["out"]==g1bn["channels"],f"{name}: head BN mismatch")
    expect(g1bn["channels"]*3==biasmul["in"]==passmul["in"],f"{name}: global pool input mismatch")
    expect(biasmul["out"]==p1bn["channels"]==p2["in"],f"{name}: policy bias mismatch")
    expect(p2["out"]==1 and passmul["out"]==1,f"{name}: v8 policy output channels must be 1")
    return {"name":name,"policy_out_channels":1}

def parse_value_v8(r:Reader,trunk_c:int):
    name=r.token()
    v1=parse_conv(r); bn=parse_bn(r); parse_activation_v8(r); v2=parse_matmul(r); b2=parse_bias(r); parse_activation_v8(r); v3=parse_matmul(r); b3=parse_bias(r); sv=parse_matmul(r); sb=parse_bias(r); own=parse_conv(r)
    expect(v1["in"]==trunk_c and v1["out"]==bn["channels"],f"{name}: value trunk mismatch")
    expect(v2["in"]==bn["channels"]*3 and v2["out"]==b2["channels"],f"{name}: value pooled mismatch")
    expect(v3["in"]==v2["out"] and v3["out"]==b3["channels"]==3,f"{name}: value output mismatch")
    expect(sv["in"]==v2["out"] and sv["out"]==sb["channels"]==4,f"{name}: v8 score output mismatch")
    expect(own["in"]==v1["out"] and own["out"]==1,f"{name}: ownership output mismatch")
    return {"name":name,"value_channels":3,"score_value_channels":4,"ownership_channels":1}

def parse_decompressed(data:bytes,source_sha256:str|None=None):
    r=Reader(data)
    model_name=r.token(); version=r.integer()
    expect(version==8,f"production parser specialization requires model version 8, got {version}")
    num_input=r.integer(); num_global=r.integer()
    expect(num_input>0 and num_global>0,"invalid model input channels")
    trunk=parse_trunk_v8(r,num_input,num_global)
    policy=parse_policy_v8(r,trunk["trunk_channels"])
    value=parse_value_v8(r,trunk["trunk_channels"])
    r.skip_ws()
    expect(r.p==len(data),f"trailing unparsed bytes: offset={r.p} size={len(data)}")
    arch={"model_name":model_name,"model_version":version,"num_input_channels":num_input,"num_input_global_channels":num_global,"trunk":trunk,"policy":policy,"value":value}
    canonical=json.dumps(arch,sort_keys=True,separators=(",",":")).encode()
    return {"schema":"pure-udon-go.katago-v8-tensor-manifest.v1","source_sha256":source_sha256,"decompressed_size_bytes":len(data),"decompressed_sha256":hashlib.sha256(data).hexdigest(),"architecture_sha256":hashlib.sha256(canonical).hexdigest(),"tensor_count":len(r.tensors),"tensor_element_count":sum(t.element_count for t in r.tensors),"tensor_bytes":sum(t.byte_count for t in r.tensors),"architecture":arch,"tensors":[asdict(t) for t in r.tensors],"bytes_consumed":r.p}

def parse_model(path:Path):
    compressed=path.read_bytes()
    try:data=gzip.decompress(compressed)
    except Exception as e: raise ParseError(f"invalid gzip: {e}") from e
    result=parse_decompressed(data,hashlib.sha256(compressed).hexdigest())
    result["source_path"]=path.as_posix(); result["compressed_size_bytes"]=len(compressed)
    return result

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--model",required=True,type=Path); ap.add_argument("--output",required=True,type=Path); a=ap.parse_args()
    result=parse_model(a.model); a.output.parent.mkdir(parents=True,exist_ok=True); a.output.write_text(json.dumps(result,indent=2,sort_keys=True)+"\n",encoding="utf-8")
    print(f"model={result['architecture']['model_name']}")
    print(f"version={result['architecture']['model_version']}")
    print(f"tensor_count={result['tensor_count']}")
    print(f"tensor_element_count={result['tensor_element_count']}")
    print(f"bytes_consumed={result['bytes_consumed']}/{result['decompressed_size_bytes']}")
    print(f"architecture_sha256={result['architecture_sha256']}")
    return 0

if __name__=="__main__": raise SystemExit(main())
