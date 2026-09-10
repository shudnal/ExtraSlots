"""Temporary guarded source update; never builds or executes the mod."""
import base64
import hashlib
import json
import lzma
import re
import subprocess
from pathlib import Path
import xml.etree.ElementTree as ET
from tree_sitter import Language, Parser
import tree_sitter_c_sharp

BASE = "1af1d4bd02de54939c861e335caff20f32c1b827"
EXPECTED = {'Compatibility/BBHCompat.cs': '52ca321137fac92279c579afb315e892d51cfa36', 'DebugLogging.cs': '5a753cd3644e81739a7690036d2f211dc6c0b94e', 'DeferredInventory.cs': '72d6ba57ceb0e292264b003583a46cbff02f97b2', 'DeferredTombstone.cs': '9c6a0242540e3e282460cec972b8d4e187812ee9', 'ExtraUtilitySlots.cs': '4605fd90fb6d941dc453e6c530ae26f82fad5823', 'InventoryInteraction.cs': 'aa12402e1464bf7f472ce49ed99ce2abba5e082c', 'InventorySerialization.cs': 'cba1fab04f44f78c0b92843af8129ff4790c9b65', 'PlayerInventoryOperations.cs': '64c9fea7e79e605c67148917a78669496c577ff8', 'Slots.cs': '947070beb267d81b8d1f35ed863ae4ac95f7adce', 'TombStoneInteraction.cs': 'd57d0021ef3e90a1535baa78f80518f02ea5f153', 'docs/VALHEIM_1_0_7_INVENTORY_CONTRACTS.md': '3443ff648ebff066d42e5dc5036207b8747ef4d7', 'package/thunderstore/ExtraSlots/CHANGELOG.md': '6910e466c05c78901ff9068b2c279ab084ced972'}
PATCH_HASH = "34684f8cf014d3927844f63a503e154527c70c767bb9108890aa04a3a77c6e65"
DATA = """
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
"""

root = Path.cwd()
patch = lzma.decompress(base64.b64decode(DATA))
assert hashlib.sha256(patch).hexdigest() == PATCH_HASH, "Patch transport integrity mismatch"
paths = re.findall(r"^diff --git a/(\S+) b/\1$", patch.decode(), re.MULTILINE)
assert set(paths) == set(EXPECTED), "Unexpected patch paths"
for name in paths:
    original = subprocess.run(["git", "show", BASE + ":" + name], capture_output=True)
    target = root / name
    if original.returncode:
        assert name == "docs/VALHEIM_1_0_7_INVENTORY_CONTRACTS.md" and not target.exists(), name
    else:
        assert target.read_bytes() == original.stdout, "Concurrent source change: " + name
subprocess.run(["git", "apply", "--unidiff-zero", "--check", "-"], input=patch, check=True)
subprocess.run(["git", "apply", "--unidiff-zero", "-"], input=patch, check=True)
for name, expected in EXPECTED.items():
    content = (root / name).read_bytes()
    assert hashlib.sha1(b"blob " + str(len(content)).encode() + b"\0" + content).hexdigest() == expected, name
parser = Parser(Language(tree_sitter_c_sharp.language()))
counts = {"cs": 0, "json": 0, "xml": 0}
for path in root.rglob("*"):
    if not path.is_file() or ".git" in path.parts:
        continue
    if path.suffix == ".cs":
        tree = parser.parse(path.read_bytes())
        if tree.root_node.has_error:
            pending = [tree.root_node]
            while pending:
                node = pending.pop()
                if node.type == "ERROR" or node.is_missing:
                    print(path.relative_to(root), node.type, node.start_point, node.end_point)
                pending.extend(node.children)
            raise RuntimeError("C# syntax error: " + str(path))
        counts["cs"] += 1
    if path.suffix == ".json":
        json.loads(path.read_text(encoding="utf-8-sig"))
        counts["json"] += 1
    if path.suffix in (".csproj", ".props", ".targets"):
        ET.parse(path)
        counts["xml"] += 1
    try:
        text = path.read_text(encoding="utf-8-sig")
    except UnicodeDecodeError:
        continue
    if "translations" not in [part.lower() for part in path.parts] and path.name != "EmbeddedLocalizations.csv":
        assert not re.search(r"[\u0400-\u052f]", text), "Cyrillic outside localization: " + str(path)
    if path.suffix == ".cs":
        assert not re.search(r"new Inventory\(\s*(?:true|false|_:)", text), "Transport constructor in plugin code: " + str(path)
        for obsolete in (r"InventoryGrid\.Element\b", r"\bRPC_OpenRespons\b", r"\bRPC_TakeAllRespons\b", r"\bm_lastDataString\b"):
            assert not re.search(obsolete, text), (path, obsolete)
project = ET.parse(root / "ExtraSlots.csproj")
includes = {node.attrib["Include"].replace("\\", "/") for node in project.findall(".//{http://schemas.microsoft.com/developer/msbuild/2003}Compile")}
assert {str(path.relative_to(root)) for path in root.rglob("*.cs")} <= includes
assert 'public const string pluginVersion = "1.2.1";' in (root / "ExtraSlots.cs").read_text(encoding="utf-8-sig")
assert json.loads((root / "package/thunderstore/ExtraSlots/manifest.json").read_text())['version_number'] == '1.2.1'
# Preserve the previous StackAll fixes and the complete hotbar implementation byte for byte.
for name in ["InventoryPreventStackAll.cs"] + [str(path.relative_to(root)) for path in (root / "HotBars").glob("*.cs")]:
    original = subprocess.check_output(["git", "show", BASE + ":" + name])
    assert (root / name).read_bytes() == original, "Protected source changed: " + name
utility = (root / "ExtraUtilitySlots.cs").read_text(encoding="utf-8-sig")
assert "(ItemDrop.ItemData.ItemType)727" not in utility
assert "item.m_shared.m_itemType =" not in utility
subprocess.run(["git", "diff", "--check"], check=True)
Path(__file__).unlink()
print("Source-only validation passed:", counts)
print("Confirmed all changed blob hashes, project inclusion, version, and protected sources.")
print("No mod build, execution, or runtime tests were performed.")
