#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json, struct
from pathlib import Path
import gzip

from katago_model_parser import parse_model
MAGIC=20000630

def _cstring(data,p):
    q=data.index(0,p); return data[p:q].decode("ascii"),q+1

def read_rgba32f_exr(path:Path):
    data=path.read_bytes(); p=0; magic,version=struct.unpack_from("<II",data,p); p+=8
    if magic!=MAGIC: raise ValueError("not an OpenEXR file")
    attrs={}
    while True:
        if data[p]==0: p+=1; break
        name,p=_cstring(data,p); typ,p=_cstring(data,p); size=struct.unpack_from("<I",data,p)[0]; p+=4; attrs[name]=(typ,data[p:p+size]); p+=size
    if attrs["compression"][1]!=b"\x00": raise ValueError("only uncompressed EXR accepted")
    xmin,ymin,xmax,ymax=struct.unpack("<iiii",attrs["dataWindow"][1]); width=xmax-xmin+1; height=ymax-ymin+1
    chpayload=attrs["channels"][1]; cp=0; channels=[]
    while cp<len(chpayload) and chpayload[cp]!=0:
        name,cp=_cstring(chpayload,cp); pixel_type,plinear,xs,ys=struct.unpack_from("<iB3xii",chpayload,cp); cp+=16
        if pixel_type!=2 or xs!=1 or ys!=1: raise ValueError("unexpected channel format")
        channels.append(name)
    if channels!=["R","G","B","A"]: raise ValueError(f"unexpected channel list {channels}")
    offset_bytes=8*height
    if p+offset_bytes>len(data): raise ValueError("truncated EXR scanline offset table")
    offsets=list(struct.unpack_from("<"+"Q"*height,data,p)); atlas=bytearray(width*height*16); seen_rows=set()
    for off in offsets:
        if off+8>len(data): raise ValueError("EXR scanline offset is out of bounds")
        y,size=struct.unpack_from("<ii",data,off); q=off+8
        if y<ymin or y>ymax or y in seen_rows: raise ValueError("invalid EXR scanline row")
        if size!=width*16 or q+size>len(data): raise ValueError("unexpected scanline size")
        row=y-ymin; seen_rows.add(y)
        for c in range(4):
            plane=data[q:q+width*4]; q+=width*4
            for x in range(width): atlas[(row*width+x)*16+c*4:(row*width+x)*16+c*4+4]=plane[x*4:x*4+4]
    if len(seen_rows)!=height: raise ValueError("missing EXR scanline row")
    return width,height,bytes(atlas)

def verify(output_dir:Path, model_path:Path|None=None):
    """Verify atlas bytes against the raw model, not only manifest hashes.

    The packing manifest is treated as metadata to cross-check.  The expected
    bytes for every tensor are read again from the supplied raw model and
    compared directly with the independently decoded EXR bytes.
    """
    pm=json.loads((output_dir/"packing_manifest.json").read_text(encoding="utf-8"))
    if model_path is None:
        model_path=Path(pm.get("source_path", ""))
    if not model_path or not model_path.is_file():
        raise ValueError(f"raw model is required for independent verification: {model_path}")
    parsed=parse_model(model_path)
    compressed=model_path.read_bytes()
    raw=gzip.decompress(compressed)
    if pm.get("source_sha256")!=parsed["source_sha256"]: raise ValueError("source compressed hash mismatch")
    if pm.get("decompressed_sha256")!=parsed["decompressed_sha256"]: raise ValueError("source decompressed hash mismatch")
    if pm.get("architecture_sha256")!=parsed["architecture_sha256"]: raise ValueError("source architecture hash mismatch")
    width,height,atlas=read_rgba32f_exr(output_dir/pm["texture"]["file"])
    raw_name=pm["texture"].get("raw_file")
    if raw_name is not None:
        raw_path=output_dir/raw_name
        if not raw_path.is_file(): raise ValueError(f"raw baked texture is missing: {raw_path}")
        raw_atlas=raw_path.read_bytes()
        if raw_atlas!=atlas: raise ValueError("raw baked texture does not match independently decoded EXR")
        if pm["texture"].get("raw_sha256")!=hashlib.sha256(raw_atlas).hexdigest(): raise ValueError("raw baked texture hash mismatch")
    if width!=pm["texture"]["width"] or height!=pm["texture"]["height"]: raise ValueError("texture dimensions mismatch")
    if pm.get("tensor_count")!=len(parsed["tensors"]): raise ValueError("tensor count mismatch")
    if pm.get("tensor_element_count")!=parsed["tensor_element_count"]: raise ValueError("tensor element count mismatch")
    results=[]; failures=0
    for index,(packed,source) in enumerate(zip(pm["tensors"],parsed["tensors"])):
        for key in ("name","kind","shape","element_count"):
            if packed.get(key)!=source[key]: raise ValueError(f"tensor metadata mismatch at index {index}: {key}")
        if packed.get("source_data_sha256")!=source["data_sha256"]: raise ValueError(f"tensor metadata mismatch at index {index}: source_data_sha256")
        if packed.get("atlas_float_count")!=source["element_count"]: raise ValueError(f"tensor metadata mismatch at index {index}: atlas_float_count")
        start=packed["atlas_float_offset"]*4; end=start+packed["atlas_float_count"]*4
        if start<0 or end>len(atlas): raise ValueError(f"tensor {source['name']} atlas range is out of bounds")
        source_start=source["decompressed_offset"]; source_end=source_start+source["byte_count"]
        if source_start<0 or source_end>len(raw): raise ValueError(f"tensor {source['name']} source range is out of bounds")
        expected=raw[source_start:source_end]; recovered=atlas[start:end]
        expected_sha=hashlib.sha256(expected).hexdigest(); recovered_sha=hashlib.sha256(recovered).hexdigest(); ok=recovered==expected
        failures+=0 if ok else 1
        results.append({"name":source["name"],"kind":source["kind"],"shape":source["shape"],"byte_count":source["byte_count"],"atlas_float_offset":packed["atlas_float_offset"],"expected_sha256":expected_sha,"recovered_sha256":recovered_sha,"exact_bytes":ok})
    report={"schema":"pure-udon-go.weight-reconstruction.v2","verification":"direct raw tensor bytes versus independently decoded RGBA32F atlas bytes","source_path":model_path.as_posix(),"source_sha256":parsed["source_sha256"],"decompressed_sha256":parsed["decompressed_sha256"],"architecture_sha256":parsed["architecture_sha256"],"tensor_count":len(results),"failure_count":failures,"exact_tensor_count":len(results)-failures,"results":results}
    (output_dir/"tensor_reconstruction.json").write_text(json.dumps(report,indent=2,sort_keys=True)+"\n",encoding="utf-8")
    if failures: raise ValueError(f"{failures} tensor reconstruction failures")
    return report

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--output-dir",required=True,type=Path); ap.add_argument("--model",required=True,type=Path); a=ap.parse_args(); r=verify(a.output_dir,a.model); print(f"tensor_count={r['tensor_count']}"); print(f"failure_count={r['failure_count']}"); return 0
if __name__=="__main__": raise SystemExit(main())
