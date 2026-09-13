#!/usr/bin/env python3
from __future__ import annotations
import argparse, hashlib, json
from pathlib import Path
from texture_packer import pack_model
from verify_weights import verify

def main():
    ap=argparse.ArgumentParser(); ap.add_argument("--model",required=True,type=Path); ap.add_argument("--output-dir",required=True,type=Path); ap.add_argument("--width",type=int,default=1024); a=ap.parse_args()
    packing=pack_model(a.model,a.output_dir,a.width); report=verify(a.output_dir,a.model)
    def sha256_file(path): return hashlib.sha256(path.read_bytes()).hexdigest()
    file_names=("weights_rgba32f.exr","weights_rgba32f.raw","packing_manifest.json","tensor_manifest.json","tensor_reconstruction.json")
    runtime_asset=a.output_dir/"weights_rgba32f.asset"
    if runtime_asset.is_file(): file_names += (runtime_asset.name,)
    files={name:{"sha256":sha256_file(a.output_dir/name),"size_bytes":(a.output_dir/name).stat().st_size} for name in file_names}
    manifest={"schema":"pure-udon-go.production-model.v2","source":{"path":a.model.as_posix(),"compressed_sha256":packing["source_sha256"],"compressed_size_bytes":packing["compressed_size_bytes"],"decompressed_sha256":packing["decompressed_sha256"],"decompressed_size_bytes":packing["decompressed_size_bytes"]},"architecture":{"model_name":packing["model_name"],"model_version":packing["model_version"],"architecture_sha256":packing["architecture_sha256"]},"tensor_count":packing["tensor_count"],"tensor_element_count":packing["tensor_element_count"],"tensor_bytes":packing["tensor_bytes"],"precision":packing["precision"],"layout_schema":packing["layout_schema"],"texture":packing["texture"],"verification":{"schema":report["schema"],"exact_tensor_count":report["exact_tensor_count"],"failure_count":report["failure_count"]},"files":files}
    (a.output_dir/"ModelManifest.json").write_text(json.dumps(manifest,indent=2,sort_keys=True)+"\n",encoding="utf-8")
    print("MODEL BAKE PASS"); return 0
if __name__=="__main__": raise SystemExit(main())
