import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
LAYOUT = ROOT / "Assets" / "PureUdonGo" / "Runtime" / "GoProductionModelLayout.cs"
PACKING = ROOT / "Assets" / "PureUdonGo" / "Model" / "Generated" / "packing_manifest.json"


class RuntimeLayoutTests(unittest.TestCase):
    def test_every_production_tensor_has_the_committed_runtime_offset(self):
        source = LAYOUT.read_text(encoding="utf-8")
        constants = {name.lower(): int(value) for name, value in re.findall(r"public const int (\w+)=(\d+);", source)}
        custom = {
            "p1/intermediate_conv/w": "policyp1w",
            "g1/w": "policyg1w",
            "g1/norm.mean": "policyg1normmean",
            "g1/norm.variance": "policyg1normvariance",
            "g1/norm.bias": "policyg1normbias",
            "matmulg2w": "policyg2w",
            "p1/norm.mean": "policyp1normmean",
            "p1/norm.variance": "policyp1normvariance",
            "p1/norm.bias": "policyp1normbias",
            "p2/w": "policyp2w",
            "matmulpass": "policypassw",
            "v1/w": "valuev1w",
            "v1/norm.mean": "valuev1normmean",
            "v1/norm.variance": "valuev1normvariance",
            "v1/norm.bias": "valuev1normbias",
            "v2/w": "valuev2w",
            "v2/b": "valuev2b",
            "v3/w": "valuev3w",
            "v3/b": "valuev3b",
            "sv3/w": "scorev3w",
            "sv3/b": "scorev3b",
            "vownership/w": "ownershipw",
        }
        packing = json.loads(PACKING.read_text(encoding="utf-8"))
        missing = []
        mismatched = []
        for tensor in packing["tensors"]:
            key = custom.get(tensor["name"], re.sub(r"[^A-Za-z0-9]", "", tensor["name"]).lower())
            if key not in constants:
                missing.append((tensor["name"], key))
            elif constants[key] != tensor["atlas_float_offset"]:
                mismatched.append((tensor["name"], constants[key], tensor["atlas_float_offset"]))
        self.assertEqual(missing, [])
        self.assertEqual(mismatched, [])


if __name__ == "__main__":
    unittest.main()
