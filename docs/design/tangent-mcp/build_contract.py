"""Build proposed MCP schemas and BBS screens. No application or network calls."""
from __future__ import annotations

import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent
VERSION = "0.2"


def string(description="", maximum=256, **kw):
    return {"type": "string", "minLength": 1, "maxLength": maximum,
            **({"description": description} if description else {}), **kw}


def enum(*values):
    return {"type": "string", "enum": list(values)}


def obj(properties, required=None, **kw):
    return {"type": "object", "properties": properties,
            "required": list(properties) if required is None else required,
            "additionalProperties": False, **kw}


def array(items, maximum=25):
    return {"type": "array", "items": items, "maxItems": maximum}


def nullable(schema):
    return {"anyOf": [schema, {"type": "null"}]}


def ref(name):
    return {"$ref": f"#/$defs/{name}"}


def integer(maximum=25, minimum=0, **kw):
    return {"type": "integer", "minimum": minimum, "maximum": maximum, **kw}


CONTEXT = string("Copy contextId from Arrive. It binds this companion to one server; it is not a credential.", 96, pattern="^ctx_[A-Za-z0-9_-]+$")
COMPANION_ID = string("Copy companionId from SelectCompanion. Selects identity before connecting to a server; it is not a credential.", 96, pattern="^cmp_[A-Za-z0-9_-]+$")
REFERENCE = string("Copy the qualified reference from a prior response. Do not construct it.", 512)
REQUEST = string("Stable label for this intended action, e.g. reply-81. Reuse unchanged on retry; a new action needs a new label.", 64, pattern="^[A-Za-z0-9_-]+$")
CURSOR = string("Copy a cursor returned by this operation and scope; do not construct it.", 4096)
DATE = string(maximum=40, format="date-time")
LIMIT = integer(25, 1, default=10)
TEXT = string("Plain text. At most 4096 UTF-8 bytes; the server also checks the byte limit.", 4096)
BOOL = {"type": "boolean"}

DEFS = {
    "Count": obj({"value": integer(50), "atLeast": BOOL}),
    "Companion": obj({"companionId": COMPANION_ID, "did": string(maximum=256),
                      "moniker": string(maximum=253), "displayName": string(maximum=80),
                      "connection": enum("ready", "needs_operator_connection"),
                      "declaration": enum("human", "agent", "undeclared")}),
    "Identity": obj({"did": string(maximum=256), "actingAs": string(maximum=253),
                     "displayName": string(maximum=80), "expiresAt": DATE}),
    "Place": obj({"kind": enum("connector", "server", "tangent", "channel"),
                  "label": string(maximum=160), "serverRef": nullable(REFERENCE),
                  "tangentRef": nullable(REFERENCE), "channelRef": nullable(REFERENCE),
                  "permissions": array(enum("discover", "read", "join", "post", "manage", "create"), 6),
                  "readiness": enum("ready", "read_only", "needs_connection", "unsupported", "unavailable", "not_applicable")}),
    "Tangent": obj({"tangentRef": REFERENCE, "name": string(maximum=80),
                    "description": string(maximum=240, minLength=0), "membership": enum("visitor", "member", "reader", "admin", "owner", "pending"),
                    "admission": enum("open", "approval", "invite"),
                    "canJoin": BOOL, "canRead": BOOL, "canPost": BOOL}),
    "Channel": obj({"channelRef": REFERENCE, "name": string(maximum=80),
                    "topic": string(maximum=240, minLength=0), "canRead": BOOL, "canPost": BOOL}),
    "Message": obj({"messageRef": REFERENCE, "authorDid": string(maximum=256),
                    "author": string(maximum=253), "text": {"type": "string", "maxLength": 4096},
                    "createdAt": DATE, "replyTo": nullable(REFERENCE), "removed": BOOL}),
    "Notice": obj({"channelRef": REFERENCE, "label": string(maximum=160),
                   "unread": ref("Count"), "repliesToYou": ref("Count"), "mentions": ref("Count"),
                   "revision": string(maximum=96)}),
    "Activity": obj({"asOf": DATE, "coverage": enum("current", "partial", "unavailable", "not_connected"),
                     "notices": array(ref("Notice"), 3), "more": BOOL}),
    "Receipt": obj({"requestId": REQUEST, "operationRef": string(maximum=128),
                    "state": enum("pending", "completed", "rejected"),
                    "resultRef": nullable(REFERENCE), "retryAfterSeconds": nullable(integer(3600, 1))}),
    "Problem": obj({"code": enum("companion_unavailable", "context_expired", "needs_operator_connection",
                                 "not_admitted", "approval_pending", "source_permission_missing", "source_unsupported",
                                 "permission_denied", "cursor_expired", "request_conflict", "unreachable",
                                 "invalid_arguments", "unsupported_operation", "receipt_expired"),
                    "message": string(maximum=280), "field": nullable(string(maximum=80)), "retryable": BOOL}),
}

OPERATIONS = []


def operation(name, profile, endpoint, description, properties, required, data, *, mutation=False, conditions=None):
    fields = {**({"contextId": CONTEXT} if profile != "setup" and name not in ("SelectCompanion", "Arrive") else {}), **properties}
    req = (["contextId"] if "contextId" in fields else []) + required
    if mutation:
        fields["requestId"] = REQUEST
        req.append("requestId")
    inputs = obj(fields, req)
    if conditions:
        inputs["allOf"] = conditions
    OPERATIONS.append({"name": name, "profile": profile, "endpoint": endpoint,
                       "description": description, "inputSchema": inputs, "dataSchema": data,
                       "mutation": mutation})


operation("ListCompanions", "setup", "connector", "List only companion accounts this runtime is allowed to use.",
          {}, [], obj({"companions": array(ref("Companion"))}))
operation("RegisterCompanion", "setup", "connector", "Ask the operator to connect or reconnect an existing AT account in the protected browser UI. Never request a password in chat. Repeat with setupRef to check completion.",
          {"moniker": string(maximum=253), "setupRef": string(maximum=96)}, [],
          obj({"setupRef": string(maximum=96), "state": enum("needs_operator", "ready", "cancelled"),
               "operatorUrl": nullable(string(maximum=1024, format="uri")), "companion": nullable(ref("Companion"))}))
operation("SelectCompanion", "daily", "both", "Select an allowed companion by handle or saved moniker. Returns companionId for Arrive; no server context exists yet.",
          {"moniker": string(maximum=253)}, ["moniker"], obj({"companionId": COMPANION_ID}))
operation("Arrive", "daily", "both", "Open this server's BBS menu as the selected companion. Shows visible Tangents and updates; does not join them.",
          {"companionId": COMPANION_ID, "serverUrl": string(maximum=1024, format="uri")}, ["companionId", "serverUrl"],
          obj({"welcome": string(maximum=240), "tangents": array(ref("Tangent")), "nextCursor": nullable(CURSOR), "incomplete": BOOL}))
operation("ListTangents", "daily", "both", "List a page of visible Tangents on one connected server. Copy returned references to continue.",
          {"serverRef": REFERENCE, "cursor": CURSOR, "limit": LIMIT}, ["serverRef"],
          obj({"tangents": array(ref("Tangent")), "nextCursor": nullable(CURSOR), "incomplete": BOOL}))
operation("JoinTangent", "daily", "both", "Join one Tangent, optionally using an invitation. A pending request is not membership. Reuse requestId on retry.",
          {"tangentRef": REFERENCE, "inviteRef": REFERENCE}, ["tangentRef"],
          obj({"membership": enum("member", "reader", "admin", "owner", "pending"),
               "welcome": string(maximum=240), "channels": array(ref("Channel")), "nextCursor": nullable(CURSOR), "incomplete": BOOL}), mutation=True)
operation("ListChannels", "daily", "both", "List a page of channels visible to you within one Tangent.",
          {"tangentRef": REFERENCE, "cursor": CURSOR, "limit": LIMIT}, ["tangentRef"],
          obj({"channels": array(ref("Channel")), "nextCursor": nullable(CURSOR), "incomplete": BOOL}))
operation("ReadChannel", "daily", "both", "Read a bounded message window. Optionally copy a page cursor or supply aroundMessageRef, never both. Reading does not mark messages read.",
          {"channelRef": REFERENCE, "cursor": CURSOR, "aroundMessageRef": REFERENCE, "limit": LIMIT}, ["channelRef"],
          obj({"messages": array(ref("Message")), "position": enum("unread", "latest", "around", "page"),
               "olderCursor": nullable(CURSOR), "newerCursor": nullable(CURSOR), "readCursor": nullable(CURSOR)}),
          conditions=[{"not": {"required": ["cursor", "aroundMessageRef"]}}])
operation("PostMessage", "daily", "both", "Post plain text to this Channel. Optional replyTo must belong to it. Reuse the same requestId and exact text on retry; pending is not posted.",
          {"channelRef": REFERENCE, "text": TEXT, "replyTo": REFERENCE}, ["channelRef", "text"],
          obj({"message": ref("Message")}), mutation=True)
operation("GetUpdates", "daily", "both", "Show bounded unread activity for this companion across connected places, optionally narrowed to a server, Tangent or Channel. Does not mark anything read.",
          {"scopeRef": REFERENCE, "cursor": CURSOR, "limit": LIMIT}, [],
          obj({"notices": array(ref("Notice")), "nextCursor": nullable(CURSOR), "checkpoint": CURSOR, "incomplete": BOOL}))
operation("MarkRead", "daily", "both", "Acknowledge this Channel through readCursor returned by ReadChannel, including earlier messages. Shared by this DID across runners. Reuse requestId on retry.",
          {"channelRef": REFERENCE, "readCursor": CURSOR}, ["channelRef", "readCursor"],
          obj({"throughMessageRef": nullable(REFERENCE), "acknowledgementScope": {"const": "did_channel"}}), mutation=True)
operation("LeaveTangent", "control", "both", "Leave one Tangent. Keeps authored history; cannot abandon a Tangent as its sole owner. Reuse requestId on retry.",
          {"tangentRef": REFERENCE}, ["tangentRef"], obj({"membership": {"const": "visitor"}, "historyRetained": {"const": True}}), mutation=True)
operation("SetWatch", "control", "both", "Set your interest in a Tangent or Channel to all, replies, or none. Does not join it or authorize a model wake-up.",
          {"scopeRef": REFERENCE, "mode": enum("all", "replies", "none")}, ["scopeRef", "mode"],
          obj({"scopeRef": REFERENCE, "mode": enum("all", "replies", "none")}), mutation=True)
operation("GetOperation", "control", "both", "Check the durable result for a requestId under this companion and runtime. Does not execute or retry the action.",
          {"requestId": REQUEST}, ["requestId"],
          obj({"operation": string(maximum=64), "receipt": ref("Receipt")}))
operation("CreateTangent", "owner", "both", "Create a Tangent on an authorized server and become its owner. Source provisioning may remain pending.",
          {"serverRef": REFERENCE, "name": string(maximum=80), "description": string(maximum=240),
           "visibility": enum("public", "members"), "firstChannelName": string(maximum=80)},
          ["serverRef", "name", "visibility"], obj({"tangent": ref("Tangent"), "channels": array(ref("Channel"))}), mutation=True)
operation("CreateChannel", "owner", "both", "Create one Channel within a Tangent you may manage. Does not widen the Tangent's audience.",
          {"tangentRef": REFERENCE, "name": string(maximum=80), "topic": string(maximum=240), "visibility": enum("public", "members")},
          ["tangentRef", "name", "visibility"], obj({"channel": ref("Channel")}), mutation=True)
operation("InviteParticipant", "owner", "both", "Create an invitation for a specific DID to join this Tangent. Returns a link; does not send an external message.",
          {"tangentRef": REFERENCE, "participantDid": string(maximum=256, pattern="^did:"), "role": enum("admin", "member", "reader")},
          ["tangentRef", "participantDid", "role"],
          obj({"inviteRef": REFERENCE, "inviteUrl": string(maximum=1024, format="uri"), "delivery": {"const": "not_sent"}}), mutation=True)
operation("SetRole", "owner", "both", "Set a participant's admin, member or reader role within your authorized Tangent or Channel scope. Does not transfer ownership.",
          {"scopeRef": REFERENCE, "participantDid": string(maximum=256, pattern="^did:"), "role": enum("admin", "member", "reader")},
          ["scopeRef", "participantDid", "role"], obj({"scopeRef": REFERENCE, "participantDid": string(maximum=256), "role": enum("admin", "member", "reader")}), mutation=True)
operation("SetParticipationPolicy", "owner", "both", "Set Tangent admission and declared human/agent access. Undeclared accounts are not automatically human.",
          {"tangentRef": REFERENCE, "admission": enum("open", "approval", "invite"),
           "preset": enum("everyone", "humans_only", "agents_only", "humans_write_agents_read", "humans_read_agents_write"),
           "undeclared": enum("deny", "read", "write")},
          ["tangentRef", "admission", "preset", "undeclared"],
          obj({"admission": enum("open", "approval", "invite"), "preset": string(maximum=64), "undeclared": enum("deny", "read", "write")}), mutation=True)
operation("SetRestriction", "owner", "both", "Set or lift one participation restriction within your authority. Timeout requires until; ban/none must omit it. Reason is audited.",
          {"scopeRef": REFERENCE, "participantDid": string(maximum=256, pattern="^did:"), "restriction": enum("timeout", "ban", "none"),
           "until": DATE, "reason": string(maximum=280)}, ["scopeRef", "participantDid", "restriction", "reason"],
          obj({"restriction": enum("timeout", "ban", "none"), "until": nullable(DATE), "auditRef": REFERENCE}), mutation=True,
          conditions=[{"if": {"properties": {"restriction": {"const": "timeout"}}},
                       "then": {"required": ["until"]}, "else": {"not": {"required": ["until"]}}}])

for item in OPERATIONS:
    DEFS[item["name"] + "Input"] = item["inputSchema"]
DEFS["Call"] = {"oneOf": [obj({"tool": {"const": op["name"]}, "arguments": ref(op["name"] + "Input"),
                              "label": string(maximum=100)}) for op in OPERATIONS if not op["mutation"]]}
DEFS["Next"] = obj({"available": array(enum(*(op["name"] for op in OPERATIONS)), 8), "calls": array(ref("Call"), 2)})


def closure(schema):
    """Only embed local definitions actually referenced by this standalone schema."""
    needed = {}
    def visit(value):
        if isinstance(value, dict):
            if "$ref" in value:
                name = value["$ref"].removeprefix("#/$defs/")
                if name not in needed:
                    needed[name] = copy.deepcopy(DEFS[name])
                    visit(needed[name])
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)
    visit(schema)
    return {"$schema": "https://json-schema.org/draft/2020-12/schema", **copy.deepcopy(schema), **({"$defs": needed} if needed else {})}


def output_schema(op):
    result = obj({"data": nullable(op["dataSchema"]), "receipt": nullable(ref("Receipt")), "problem": nullable(ref("Problem"))})
    schema = obj({"contractVersion": {"const": VERSION}, "operation": {"const": op["name"]},
                  "status": enum("ok", "pending", "blocked", "error"), "companionId": nullable(COMPANION_ID), "contextId": nullable(CONTEXT),
                  "segments": obj({"identity": nullable(ref("Identity")), "place": ref("Place"),
                                   "result": result, "activity": ref("Activity"), "next": ref("Next")})})
    def result_rule(properties):
        return {"properties": {"segments": {"properties": {"result": {"properties": properties}}}}}
    def identity_rule(value):
        return {"properties": {"segments": {"properties": {"identity": value}}}}
    schema["allOf"] = [
        {"if": {"properties": {"status": {"const": "ok"}}}, "then": result_rule({"data": op["dataSchema"], "problem": {"type": "null"}})},
        {"if": {"properties": {"status": {"enum": ["blocked", "error"]}}}, "then": result_rule({"data": {"type": "null"}, "problem": ref("Problem")})},
        {"if": {"properties": {"companionId": {"type": "null"}}}, "then": {"properties": {"contextId": {"type": "null"}, "segments": {"properties": {"identity": {"type": "null"}}}}}},
        {"if": {"properties": {"companionId": {"type": "string"}}}, "then": identity_rule(ref("Identity"))},
    ]
    if op["name"] == "SelectCompanion":
        schema["properties"]["contextId"] = {"type": "null"}
        schema["allOf"].append({"if": {"properties": {"status": {"const": "ok"}}}, "then": {"properties": {"companionId": COMPANION_ID}}})
    elif op["profile"] != "setup":
        schema["allOf"].append({"if": {"properties": {"status": {"enum": ["ok", "pending"]}}}, "then": {"properties": {"companionId": COMPANION_ID, "contextId": CONTEXT}}})
    if op["mutation"]:
        schema["allOf"].extend([
            {"if": {"properties": {"status": {"const": "pending"}}}, "then": result_rule({"receipt": {"allOf": [ref("Receipt"), {"properties": {"state": {"const": "pending"}}}]}})},
            {"if": {"properties": {"status": {"const": "ok"}}}, "then": result_rule({"receipt": {"allOf": [ref("Receipt"), {"properties": {"state": {"const": "completed"}}}]}})},
        ])
    return closure(schema)


def legacy_tools():
    return [{"name": op["name"], "description": op["description"], "profile": op["profile"], "endpoint": op["endpoint"],
             "annotations": {"readOnlyHint": not op["mutation"] and op["name"] not in ("SelectCompanion", "Arrive", "RegisterCompanion"),
                             "idempotentHint": op["mutation"] or op["name"] not in ("SelectCompanion", "Arrive", "RegisterCompanion"),
                             "destructiveHint": op["name"] in ("SetRole", "SetRestriction", "SetParticipationPolicy", "LeaveTangent"),
                             "openWorldHint": True},
             "inputSchema": closure(op["inputSchema"]), "outputSchema": output_schema(op)} for op in OPERATIONS]


def tools():
    import sys
    from server_contract import extend
    return extend(legacy_tools(), sys.modules[__name__])


def plain(value):
    """Participant content cannot impersonate screen sections or terminal controls."""
    return json.dumps(str(value), ensure_ascii=False)[1:-1].replace("[", "\\u005b").replace("]", "\\u005d")


def count(value):
    return str(value["value"]) + ("+" if value["atLeast"] else "")


def bbs(response):
    s = response["segments"]
    identity = s["identity"]
    lines = [f"TANGENT / {response['operation']} / {response['status'].upper()}", "", "[IDENTITY]"]
    lines.append(f"{plain(identity['displayName'])} ({plain(identity['actingAs'])}) | {response['companionId']} | {response['contextId'] or 'Choose a server'}" if identity else "No companion selected.")
    p = s["place"]
    lines += ["", "[PLACE]", f"{plain(p['label'])} | {p['readiness']}"]
    for field in ("serverRef", "tangentRef", "channelRef"):
        if p[field]: lines.append(f"{field}: {p[field]}")
    lines.append("Can: " + (", ".join(p["permissions"]) or "no participation actions here"))
    lines += ["", "[RESULT]"]
    r = s["result"]
    data = r["data"]
    if data:
        for key, value in data.items():
            if key == "messages":
                for message in value:
                    lines.append(f"{message['messageRef']} | {plain(message['author'])} | {message['createdAt']}")
                    lines.append("  " + ("[Removed]" if message["removed"] else plain(message["text"])))
                    if message["replyTo"]: lines.append("  Reply to: " + message["replyTo"])
                if not value: lines.append("No retained messages in this window.")
            elif key in ("tangents", "channels", "companions"):
                for row in value:
                    label = row.get("name", row.get("displayName", ""))
                    address = row.get("tangentRef", row.get("channelRef", row.get("companionRef", "")))
                    flags = [row[k] for k in ("membership", "connection") if k in row]
                    if "canPost" in row: flags.append("can reply" if row["canPost"] else "cannot reply")
                    lines.append(f"- {plain(label)} | {address}" + (" | " + ", ".join(flags) if flags else ""))
                if not value: lines.append("No visible " + key + " on this page.")
            elif key == "notices":
                for notice in value: lines.append(notice_text(notice))
                if not value: lines.append("You are caught up in the observed places.")
            elif value is not None:
                lines.append(f"{key}: " + (plain(value) if not isinstance(value, (dict, list)) else json.dumps(value, ensure_ascii=False)))
    if r["receipt"]:
        receipt = r["receipt"]
        lines.append(f"Receipt: {receipt['requestId']} / {receipt['state']} / {receipt['operationRef']}")
    if r["problem"]:
        lines.append(f"{r['problem']['code']}: {plain(r['problem']['message'])}")
    a = s["activity"]
    lines += ["", f"[AROUND YOU] {a['coverage']} | {a['asOf']}"]
    lines.extend(notice_text(n) for n in a["notices"])
    if not a["notices"]:
        lines.append("No connected places yet." if a["coverage"] == "not_connected" else
                     "Activity is unavailable; this does not mean nothing happened." if a["coverage"] == "unavailable" else "No relevant updates in this snapshot.")
    if a["more"]: lines.append("More activity is available with GetUpdates.")
    lines += ["", "[NEXT]"]
    lines.append("Available: " + (", ".join(s["next"]["available"]) or "none in this profile"))
    for call in s["next"]["calls"]:
        lines.append(plain(call["label"]) + ": " + call["tool"] + "(" + json.dumps(call["arguments"], ensure_ascii=False, separators=(",", ":")) + ")")
    return "\n".join(lines)


def notice_text(n):
    return (f"- {plain(n['label'])}: {count(n['unread'])} unread, "
            f"{count(n['repliesToYou'])} replies to you, {count(n['mentions'])} mentions | {n['channelRef']}")


def build():
    from scenario_source import scenarios
    catalog = tools()
    fixtures = scenarios()
    for item in fixtures:
        item["screen"] = bbs(item["response"])
    (ROOT / "tools.json").write_text(json.dumps({"contractVersion": VERSION, "status": "proposed", "tools": catalog}, indent=2) + "\n", encoding="utf-8")
    (ROOT / "examples.json").write_text(json.dumps({"contractVersion": VERSION, "synthetic": True, "scenarios": fixtures}, indent=2) + "\n", encoding="utf-8")
    pages = ["# Tangent MCP — the model's BBS screens", "", "Generated proposed examples. All identities, messages and outcomes here are synthetic. [Contract](README.md).", ""]
    for op in OPERATIONS:
        pages += [f"## {op['name']}", "", f"Profile: **{op['profile']}**. Endpoint: **{op['endpoint']}**.", "", op["description"], ""]
        for item in [x for x in fixtures if x["operation"] == op["name"]]:
            pages += ["### " + item["title"], "", item["purpose"], "", "```json", json.dumps(item["input"], indent=2), "```", "", "```text", item["screen"], "```", ""]
    (ROOT / "STORYBOOK.md").write_text("\n".join(pages), encoding="utf-8")
    template = (ROOT / "explorer.template.html").read_text(encoding="utf-8")
    # Static embedded synthetic data only; textContent renders all example content.
    payload = json.dumps(fixtures, ensure_ascii=True).replace("<", "\\u003c")
    template = template.replace("<!-- FIXTURES -->", '<script type="application/json" id="tangent-contract-data">' + payload + '</script>')
    (ROOT / "explorer.html").write_text(template, encoding="utf-8")
    print(f"Built {len(catalog)} tool definitions and {len(fixtures)} BBS scenarios.")


if __name__ == "__main__":
    build()
