import gzip
import hashlib
import json
import tempfile
import unittest
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from inspect_model_header import MARKER, probe_model


class InspectModelHeaderTests(unittest.TestCase):
    def test_probes_complete_binary_model_prefix(self):
        payload = (
            b"testmodel 8 22 19 trunk 10 128 layer 3 3 22 128 1 1\n"
            + MARKER
            + bytes(64)
        )
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "model.bin.gz"
            path.write_bytes(gzip.compress(payload))
            result = probe_model(path)

        self.assertEqual(result["model_name_token"], "testmodel")
        self.assertEqual(result["model_version_token"], 8)
        self.assertEqual(result["header_token_count"], 14)
        self.assertEqual(result["first_binary_marker_offset"], payload.index(MARKER))
        self.assertEqual(result["decompressed_sha256"], hashlib.sha256(payload).hexdigest())

    def test_rejects_non_gzip_input(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "model.bin.gz"
            path.write_bytes(b"not gzip")
            with self.assertRaisesRegex(ValueError, "valid complete gzip stream"):
                probe_model(path)

    def test_rejects_missing_binary_marker(self):
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "model.bin.gz"
            path.write_bytes(gzip.compress(b"model 8 22 19 no binary tensor here"))
            with self.assertRaisesRegex(ValueError, "@BIN@"):
                probe_model(path)

    def test_rejects_non_ascii_structure_prefix(self):
        payload = b"model 8 22 \xff " + MARKER + bytes(4)
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "model.bin.gz"
            path.write_bytes(gzip.compress(payload))
            with self.assertRaisesRegex(ValueError, "not ASCII"):
                probe_model(path)


if __name__ == "__main__":
    unittest.main()
