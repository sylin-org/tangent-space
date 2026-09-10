"""Offline specification checks, not a substitute for real MCP/auth integration tests."""
from copy import deepcopy
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parent
catalog = json.loads((ROOT / "tools.json").read_text(encoding="utf-8"))
examples = json.loads((ROOT / "examples.json").read_text(encoding="utf-8"))["scenarios"]
tools = {item["name"]: item for item in catalog["tools"]}
checks = 0


def validate(schema, value):
    global checks
    Draft202012Validator.check_schema(schema)
    Draft202012Validator(schema, format_checker=FormatChecker()).validate(value)
    checks += 1


def rejects(schema, value):
    global checks
    assert not Draft202012Validator(schema, format_checker=FormatChecker()).is_valid(value), value
    checks += 1


for case in examples:
    tool = tools[case["operation"]]
    validate(tool["inputSchema"], case["input"])
    validate(tool["outputSchema"], case["response"])
    r = case["response"]
    s = r["segments"]
    assert list(s) == ["identity", "place", "result", "activity", "next"]
    if r["contextId"] is not None and case["operation"] not in ("SelectCompanion", "Arrive"):
        assert r["contextId"] == case["input"]["contextId"]
    for call in s["next"]["calls"]:
        assert call["tool"] in s["next"]["available"]
        validate(tools[call["tool"]]["inputSchema"], call["arguments"])
        if "contextId" in call["arguments"]:
            assert call["arguments"]["contextId"] == r["contextId"]
    if s["result"]["receipt"]:
        assert s["result"]["receipt"]["requestId"] == case["input"]["requestId"]
    if r["companionId"] is None:
        assert s["identity"] is None and not s["activity"]["notices"]
    for segment in ("identity", "place", "activity", "next"):
        assert len(json.dumps(s[segment], ensure_ascii=False).encode("utf-8")) <= 4096
    assert len(json.dumps(s["result"]["data"], ensure_ascii=False).encode("utf-8")) <= 16384

assert set(tools) == {x["operation"] for x in examples}
assert sum(t["profile"] == "daily" for t in tools.values()) == 9

# Independent negative cases exercise the safety-relevant contract boundaries.
read = next(c for c in examples if c["operation"] == "ReadChannel")
post = next(c for c in examples if c["operation"] == "PostMessage")
restriction = next(c for c in examples if c["operation"] == "SetRestriction")
rejects(tools["ReadChannel"]["inputSchema"], {**read["input"], "cursor": "page", "aroundMessageRef": "message"})
rejects(tools["ReadChannel"]["inputSchema"], {**read["input"], "limit": 1000})
rejects(tools["ReadChannel"]["inputSchema"], {"channelRef": "channel_without_identity"})
rejects(tools["ReadChannel"]["inputSchema"], {**read["input"], "accessToken": "should-not-be-an-argument"})
rejects(tools["PostMessage"]["inputSchema"], {k: v for k, v in post["input"].items() if k != "requestId"})
rejects(tools["PostMessage"]["inputSchema"], {**post["input"], "text": ""})
rejects(tools["PostMessage"]["inputSchema"], {**post["input"], "text": "a" * 4097})
rejects(tools["SetRestriction"]["inputSchema"], {k: v for k, v in restriction["input"].items() if k != "until"})
rejects(tools["SetRestriction"]["inputSchema"], {**restriction["input"], "restriction": "none"})
bad = deepcopy(post["response"])
bad["segments"]["result"]["receipt"]["state"] = "pending"
rejects(tools["PostMessage"]["outputSchema"], bad)
bad = deepcopy(read["response"])
bad["companionId"] = None
rejects(tools["ReadChannel"]["outputSchema"], bad)
bad = deepcopy(read["response"])
bad["segments"]["activity"]["notices"] *= 4
rejects(tools["ReadChannel"]["outputSchema"], bad)
bad = deepcopy(read["response"])
bad["segments"]["next"]["calls"] = [{"tool": "PostMessage", "arguments": post["input"], "label": "Post this without thinking"}]
rejects(tools["ReadChannel"]["outputSchema"], bad)

rejects(tools["Arrive"]["inputSchema"], {"contextId": "ctx_old", "serverUrl": "https://tangent.example"})
rejects(tools["ReadChannel"]["inputSchema"], {**read["input"], "contextId": "cmp_lumen"})
select = next(c for c in examples if c["operation"] == "SelectCompanion" and c["response"]["status"] == "ok")
bad = deepcopy(select["response"])
bad["contextId"] = "ctx_premature"
rejects(tools["SelectCompanion"]["outputSchema"], bad)

# No remote $refs are required for a tool to be advertised or validated.
def local_refs(value):
    if isinstance(value, dict):
        if "$ref" in value: assert value["$ref"].startswith("#/$defs/")
        for child in value.values(): local_refs(child)
    elif isinstance(value, list):
        for child in value: local_refs(child)
local_refs(catalog)
print(f"PASS: {len(tools)} tools, {len(examples)} scenarios, {checks} schema/continuation/negative checks; 9 daily operations.")

# Optional evidence files validate actual HTTP tool payloads with the same contract.
for filename in sys.argv[1:]:
    evidence = json.loads(Path(filename).read_text(encoding="utf-8"))
    before = checks
    for case in evidence["calls"]:
        tool = tools[case["operation"]]
        validate(tool["inputSchema"], case["input"])
        validate(tool["outputSchema"], case["response"])
        for call in case["response"]["segments"]["next"]["calls"]:
            validate(tools[call["tool"]]["inputSchema"], call["arguments"])
    print(f"PASS: {filename}: {len(evidence['calls'])} real calls, {checks - before} schema checks.")
