#!/usr/bin/env python3
from __future__ import annotations
import argparse, gzip, json, struct, hashlib
from pathlib import Path
from katago_model_parser import parse_model
MAGIC=20000630; VERSION=2; CHANNELS=("R","G","B","A")
def _attr(name,typ,payload): return name.encode()+b"\0"+typ.encode()+b"\0"+struct.pack("<I",len(payload))+payload
def _chlist():
    out=bytearray()
    for name in CHANNELS:
        out+=name.encode()+b"\0"; out+=struct.pack("<iB3xii",2,0,1,1)
    out+=b"\0"; return bytes(out)
def write_rgba32f_exr(path:Path,width:int,height:int,interleaved:bytes):
    expected=width*height*16
    if len(interleaved)!=expected: raise ValueError(f"atlas byte size {len(interleaved)} != {expected}")
    header=bytearray(struct.pack("<II",MAGIC,VERSION)); header+=_attr("channels","chlist",_chlist()); header+=_attr("compression","compression",b"\x00")
    box=struct.pack("<iiii",0,0,width-1,height-1); header+=_attr("dataWindow","box2i",box); header+=_attr("displayWindow","box2i",box); header+=_attr("lineOrder","lineOrder",b"\x00"); header+=_attr("pixelAspectRatio","float",struct.pack("<f",1.0)); header+=_attr("screenWindowCenter","v2f",struct.pack("<ff",0.0,0.0)); header+=_attr("screenWindowWidth","float",struct.pack("<f",1.0)); header+=b"\0"
    row_payload=width*16; block_size=8+row_payload; data_start=len(header)+height*8
    with path.open("wb") as f:
        f.write(header)
        for y in range(height): f.write(struct.pack("<Q",data_start+y*block_size))
        for y in range(height):
            row=interleaved[y*row_payload:(y+1)*row_payload]; f.write(struct.pack("<ii",y,row_payload))
            for c in range(4):
                for x in range(width): f.write(row[x*16+c*4:x*16+c*4+4])
def pack_model(model_path:Path,output_dir:Path,width:int=1024):
    parsed=parse_model(model_path); raw=gzip.decompress(model_path.read_bytes()); total=sum(t["element_count"] for t in parsed["tensors"]); pixels=(total+3)//4; height=(pixels+width-1)//width; capacity=width*height*4; atlas=bytearray(capacity*4); cursor=0; entries=[]
    for t in parsed["tensors"]:
        n=t["byte_count"]; src=t["decompressed_offset"]; tensor=raw[src:src+n]; start=cursor; atlas[start*4:start*4+n]=tensor; entries.append({"name":t["name"],"kind":t["kind"],"shape":t["shape"],"element_count":t["element_count"],"source_data_sha256":t["data_sha256"],"atlas_float_offset":start,"atlas_float_count":t["element_count"]}); cursor+=t["element_count"]
    output_dir.mkdir(parents=True,exist_ok=True); atlas_bytes=bytes(atlas); exr=output_dir/"weights_rgba32f.exr"; write_rgba32f_exr(exr,width,height,atlas_bytes); exr_sha=hashlib.sha256(exr.read_bytes()).hexdigest(); raw_file=output_dir/"weights_rgba32f.raw"; raw_file.write_bytes(atlas_bytes); raw_sha=hashlib.sha256(atlas_bytes).hexdigest()
    packing={"schema":"pure-udon-go.weight-packing.v2","source_path":model_path.as_posix(),"source_sha256":parsed["source_sha256"],"compressed_size_bytes":parsed["compressed_size_bytes"],"decompressed_sha256":parsed["decompressed_sha256"],"decompressed_size_bytes":parsed["decompressed_size_bytes"],"architecture_sha256":parsed["architecture_sha256"],"model_name":parsed["architecture"]["model_name"],"model_version":parsed["architecture"]["model_version"],"tensor_count":parsed["tensor_count"],"tensor_element_count":parsed["tensor_element_count"],"tensor_bytes":parsed["tensor_bytes"],"precision":"float32","layout_schema":{"name":"sequential source-order float32 values into RGBA pixels","channel_order":"RGBA","texel_order":"row-major","float_lane_order":"R,G,B,A","float_offset_unit":"float32","padding":"zero-filled tail"},"texture":{"file":exr.name,"raw_file":raw_file.name,"runtime_asset_file":"weights_rgba32f.asset","width":width,"height":height,"channels":"RGBA","pixel_type":"FLOAT","compression":"NONE","sha256":exr_sha,"raw_sha256":raw_sha,"raw_size_bytes":len(atlas_bytes)},"used_float_count":cursor,"capacity_float_count":capacity,"tensors":entries}
    (output_dir/"packing_manifest.json").write_text(json.dumps(packing,indent=2,sort_keys=True)+"\n",encoding="utf-8"); (output_dir/"tensor_manifest.json").write_text(json.dumps(parsed,indent=2,sort_keys=True)+"\n",encoding="utf-8"); return packing
def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--model",required=True,type=Path); ap.add_argument("--output-dir",required=True,type=Path); ap.add_argument("--width",type=int,default=1024); a=ap.parse_args(); p=pack_model(a.model,a.output_dir,a.width); print(f"texture={p['texture']['width']}x{p['texture']['height']} RGBA32F"); print(f"tensor_count={p['tensor_count']}"); print(f"used_floats={p['used_float_count']}"); print(f"texture_sha256={p['texture']['sha256']}"); return 0
if __name__=="__main__": raise SystemExit(main())
