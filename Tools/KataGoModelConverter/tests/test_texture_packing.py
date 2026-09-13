import gzip, json, struct, tempfile, unittest
from pathlib import Path
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from texture_packer import pack_model
from verify_weights import verify, read_rgba32f_exr
from test_katago_model_parser import model_bytes

class BakeTests(unittest.TestCase):
    def test_fp32_exr_round_trip_exact(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td); model=root/"m.bin.gz"; model.write_bytes(gzip.compress(model_bytes())); out=root/"out"; p=pack_model(model,out,width=8); r=verify(out,model)
            self.assertEqual(r["failure_count"],0); self.assertEqual(r["tensor_count"],p["tensor_count"]); self.assertTrue(all(x["exact_bytes"] for x in r["results"]))
            w,h,atlas=read_rgba32f_exr(out/"weights_rgba32f.exr"); self.assertEqual((w,h),(8,p["texture"]["height"])); self.assertEqual(len(atlas),w*h*16); self.assertEqual((out/"weights_rgba32f.raw").read_bytes(),atlas)

    def test_direct_raw_comparison_rejects_tampered_atlas(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td); model=root/"m.bin.gz"; model.write_bytes(gzip.compress(model_bytes())); out=root/"out"; pack_model(model,out,width=8)
            exr=bytearray((out/"weights_rgba32f.exr").read_bytes()); p=8
            while exr[p]!=0:
                end=exr.index(0,p); p=end+1; end=exr.index(0,p); p=end+1; size=struct.unpack_from("<I",exr,p)[0]; p+=4+size
            p+=1; first_scanline=struct.unpack_from("<Q",exr,p)[0]; exr[first_scanline+8] ^= 1; (out/"weights_rgba32f.exr").write_bytes(exr)
            with self.assertRaisesRegex(ValueError,"raw baked texture|tensor reconstruction failures"): verify(out,model)

    def test_verifier_does_not_trust_manifest_tensor_hashes(self):
        with tempfile.TemporaryDirectory() as td:
            root=Path(td); model=root/"m.bin.gz"; model.write_bytes(gzip.compress(model_bytes())); out=root/"out"; pack_model(model,out,width=8)
            manifest=json.loads((out/"packing_manifest.json").read_text())
            manifest["tensors"][0]["source_data_sha256"]="0"*64
            (out/"packing_manifest.json").write_text(json.dumps(manifest))
            with self.assertRaisesRegex(ValueError,"source_data_sha256|tensor reconstruction failures"):
                verify(out,model)

if __name__=="__main__": unittest.main()
