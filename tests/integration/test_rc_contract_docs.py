"""Keep the guide's closed operation list synchronized with the native contract."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


class RemoteContractDocumentationTests(unittest.TestCase):
    def test_closed_operation_list_matches_native_contract(self):
        source = (ROOT / "src/Tlachihuani.RC/Contract.cs").read_text(encoding="utf-8-sig")
        dictionary = source.split("Operations = new Dictionary", 1)[1].split("};", 1)[0]
        implemented = set(re.findall(r'\["([a-z.]+)"\]\s*=', dictionary))
        guide = (ROOT / "docs/remote-controller.md").read_text(encoding="utf-8-sig")
        paragraph = guide.split("The closed operation set is ", 1)[1].split(";", 1)[0]
        documented = set(re.findall(r"`([a-z.]+)`", paragraph))
        self.assertTrue(implemented, "The native operation dictionary was not found")
        self.assertEqual(implemented, documented)


if __name__ == "__main__":
    unittest.main(verbosity=2)
