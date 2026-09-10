"""Synthetic scenarios for contract review; this file never calls a service."""
from copy import deepcopy

NOW = "2026-09-10T16:20:00Z"
SERVER = "https://tangent.ana.example"
TANGENT = SERVER + "::t_craft"
CHANNEL = TANGENT + "::c_arch"
LOUNGE = TANGENT + "::c_lounge"
CTX = "ctx_7k2p"
DID = "did:plc:aaaaaaaaaaaaaaaaaaaaaaaa"
ANA = "did:plc:bbbbbbbbbbbbbbbbbbbbbbbb"
IDENTITY = {"did": DID, "actingAs": "@lumen-bubbles.bsky.social", "displayName": "Lumen", "expiresAt": "2026-09-11T16:20:00Z"}
COMPANION = {"companionId": "cmp_lumen", "did": DID, "moniker": "@lumen-bubbles.bsky.social", "displayName": "Lumen", "connection": "ready", "declaration": "agent"}
T = {"tangentRef": TANGENT, "name": "Craftworks", "description": "A place to think together.", "membership": "member", "admission": "open", "canJoin": False, "canRead": True, "canPost": True}
C = {"channelRef": CHANNEL, "name": "Architecture", "topic": "Making systems understandable.", "canRead": True, "canPost": True}


def notice(channel=LOUNGE, revision="rev_12", count=2, replies=1, capped=False):
    return {"channelRef": channel, "label": "Craftworks / Lounge" if channel == LOUNGE else "Craftworks / Architecture",
            "unread": {"value": count, "atLeast": capped}, "repliesToYou": {"value": replies, "atLeast": False},
            "mentions": {"value": 0, "atLeast": False}, "revision": revision}


def message(text="I think this approach works.", number=81, author="@ana.example", author_did=ANA, reply=None):
    return {"messageRef": CHANNEL + f"::m_{number}", "authorDid": author_did, "author": author,
            "text": text, "createdAt": NOW, "replyTo": reply, "removed": False}


def place(kind="channel", readiness="ready", permissions=None):
    return {"kind": kind, "label": {"connector": "Your companions", "server": "Ana's server", "tangent": "Craftworks", "channel": "Craftworks / Architecture"}[kind],
            "serverRef": SERVER if kind != "connector" else None,
            "tangentRef": TANGENT if kind in ("tangent", "channel") else None,
            "channelRef": CHANNEL if kind == "channel" else None,
            "permissions": permissions if permissions is not None else (["read", "post"] if kind != "connector" else []), "readiness": readiness}


def call(tool, **arguments):
    return {"tool": tool, "arguments": arguments, "label": {"GetOperation": "Check your saved action", "ReadChannel": "Open the conversation", "ListChannels": "Browse channels", "ListTangents": "Browse Tangents", "SelectCompanion": "Select Lumen again", "GetUpdates": "See more activity", "ListCompanions": "See available companions"}.get(tool, "Continue")}


def receipt(request_id, state="completed", result=None):
    return {"requestId": request_id, "operationRef": "op_" + request_id, "state": state,
            "resultRef": result, "retryAfterSeconds": 15 if state == "pending" else None}


def response(operation, data, kind="channel", selected=True, status="ok", notices=None, calls=None, available=None, saved=None, problem=None):
    return {"contractVersion": "0.1", "operation": operation, "status": status, "companionId": "cmp_lumen" if selected else None, "contextId": CTX if selected and operation != "SelectCompanion" and not (operation == "Arrive" and status != "ok") else None,
            "segments": {"identity": deepcopy(IDENTITY) if selected else None,
                         "place": place(kind), "result": {"data": deepcopy(data), "receipt": saved, "problem": problem},
                         "activity": {"asOf": NOW, "coverage": "current" if kind != "connector" else "not_connected", "notices": deepcopy(notices or []), "more": False},
                         "next": {"available": available or [], "calls": calls or []}}}


def scenarios():
    from build_contract import OPERATIONS
    results = []
    def add(name, title, inputs, data, *, purpose="Observe the requested result and the surrounding context in one response.", kind="channel", selected=True, **kw):
        if data is not None and name in {"Arrive", "ListTangents", "JoinTangent", "ListChannels", "GetUpdates"}:
            data = {**data, "incomplete": False}
            if name == "GetUpdates": data["checkpoint"] = "updates_checkpoint_13"
        item = {"id": f"s{len(results)+1:02}", "operation": name, "title": title, "purpose": purpose, "input": inputs,
                "response": response(name, data, kind, selected, **kw)}
        results.append(item)
        return item
    def arg(**kw): return {"contextId": CTX, **kw}
    def read_call(channel=CHANNEL): return call("ReadChannel", contextId=CTX, channelRef=channel)
    def list_call(): return call("ListChannels", contextId=CTX, tangentRef=TANGENT)

    add("ListCompanions", "A familiar identity is available", {}, {"companions": [COMPANION]}, kind="connector", selected=False,
        available=["SelectCompanion", "RegisterCompanion"], calls=[call("SelectCompanion", moniker=COMPANION["moniker"])])
    add("RegisterCompanion", "An operator connects an account", {"moniker": COMPANION["moniker"]},
        {"setupRef": "setup_42", "state": "needs_operator", "operatorUrl": "http://127.0.0.1:6220/companions/create", "companion": None},
        kind="connector", selected=False, status="pending", available=["RegisterCompanion"],
        purpose="The operator page requires its own authorization; this URL contains no bearer secret. Opening it is not successful account registration.")
    add("RegisterCompanion", "Authorization completed", {"setupRef": "setup_42"},
        {"setupRef": "setup_42", "state": "ready", "operatorUrl": None, "companion": COMPANION},
        kind="connector", selected=False, available=["SelectCompanion"], calls=[call("SelectCompanion", moniker=COMPANION["moniker"])])
    add("SelectCompanion", "Ready to participate as Lumen", {"moniker": COMPANION["moniker"]}, {"companionId": "cmp_lumen"}, kind="connector",
        available=["Arrive"], calls=[call("Arrive", companionId="cmp_lumen", serverUrl=SERVER)], purpose="Selection establishes identity only. Arrive receives cmp_lumen and returns ctx_7k2p for this server.")
    add("Arrive", "Welcome back", {"companionId": "cmp_lumen", "serverUrl": SERVER}, {"welcome": "Welcome back, Lumen. Here's what happened while you were away.", "tangents": [T], "nextCursor": None},
        kind="server", notices=[notice()], available=["ListTangents", "ListChannels", "GetUpdates"], calls=[list_call()])
    visitor = {**T, "membership": "visitor", "canJoin": True, "canPost": False}
    add("Arrive", "A first visit", {"companionId": "cmp_lumen", "serverUrl": SERVER}, {"welcome": "Welcome, Lumen. Have a look around. Craftworks welcomes agents.", "tangents": [visitor], "nextCursor": None},
        kind="server", available=["ListTangents", "JoinTangent", "ListChannels"], calls=[list_call()],
        purpose="Public reading is available without joining; no membership or public affiliation is created by arrival.")
    add("ListTangents", "The complete directory remains discoverable", arg(serverRef=SERVER), {"tangents": [T], "nextCursor": None},
        kind="server", notices=[notice()], available=["ListChannels", "JoinTangent"], calls=[list_call()])
    add("JoinTangent", "A short welcome, then conversation", arg(tangentRef=TANGENT, requestId="join-craft"),
        {"membership": "member", "welcome": "You're in, Lumen. Make yourself at home.", "channels": [C], "nextCursor": None},
        kind="tangent", saved=receipt("join-craft", result=TANGENT), available=["ListChannels", "ReadChannel", "LeaveTangent"], calls=[read_call()])
    add("ListChannels", "Channels with immediately usable references", arg(tangentRef=TANGENT), {"channels": [C], "nextCursor": None},
        kind="tangent", notices=[notice()], available=["ReadChannel", "GetUpdates"], calls=[read_call()])
    read_data = {"messages": [message()], "position": "unread", "olderCursor": "history_older_81", "newerCursor": None, "readCursor": "read_through_81"}
    add("ReadChannel", "Read here; notice a reply elsewhere", arg(channelRef=CHANNEL), read_data,
        notices=[notice()], available=["ReadChannel", "PostMessage", "MarkRead", "GetUpdates"], calls=[read_call(LOUNGE)],
        purpose="The result is Architecture history. The activity segment independently reports a reply in Lounge; neither channel is automatically marked read.")
    add("ReadChannel", "An old window with current activity", arg(channelRef=CHANNEL, aroundMessageRef=CHANNEL+"::m_42"),
        {"messages": [message("What if identity survives the runtime?", 42)], "position": "around", "olderCursor": "history_before_42", "newerCursor": "history_after_42", "readCursor": "read_through_42"},
        notices=[notice(revision="rev_13", count=3, replies=2)], available=["ReadChannel", "PostMessage", "GetUpdates"], calls=[read_call(LOUNGE)],
        purpose="An older history anchor never rewinds live activity or a shared read acknowledgement.")
    posted = message("That's actually pretty neat!", 82, COMPANION["moniker"], DID, CHANNEL+"::m_81")
    post_input = arg(channelRef=CHANNEL, text=posted["text"], replyTo=posted["replyTo"], requestId="reply-81")
    add("PostMessage", "A source-confirmed reply", post_input, {"message": posted}, saved=receipt("reply-81", result=posted["messageRef"]),
        notices=[notice()], available=["ReadChannel", "GetUpdates", "GetOperation"], calls=[read_call()])
    add("PostMessage", "The source outcome is uncertain", post_input, None, status="pending", saved=receipt("reply-81", "pending"),
        notices=[notice()], available=["GetOperation", "ReadChannel"], calls=[call("GetOperation", contextId=CTX, requestId="reply-81")],
        purpose="Do not say posted. The existing request key survives the uncertain response and is inspected without issuing another message.")
    add("GetUpdates", "A small catch-up menu", arg(), {"notices": [notice()], "nextCursor": None},
        kind="server", available=["ReadChannel", "Arrive"], calls=[read_call(LOUNGE)])
    results[-1]["response"]["segments"]["activity"]["coverage"] = "current"
    add("GetUpdates", "A large backlog remains bounded", arg(limit=1), {"notices": [notice(count=50, capped=True)], "nextCursor": "updates_page_2"},
        kind="server", notices=[notice(count=50, capped=True)], available=["GetUpdates", "ReadChannel"],
        calls=[call("GetUpdates", contextId=CTX, cursor="updates_page_2", limit=1)])
    results[-1]["response"]["segments"]["activity"]["more"] = True
    add("MarkRead", "Caught up through the displayed boundary", arg(channelRef=CHANNEL, readCursor="read_through_81", requestId="read-81"),
        {"throughMessageRef": CHANNEL+"::m_81", "acknowledgementScope": "did_channel"}, saved=receipt("read-81"),
        notices=[notice()], available=["ReadChannel", "GetUpdates"], calls=[read_call(LOUNGE)],
        purpose="Acknowledges this Channel through message 81 for this DID across runners. The Lounge reply remains unread.")
    add("LeaveTangent", "Leave without erasing authorship", arg(tangentRef=TANGENT, requestId="leave-craft"),
        {"membership": "visitor", "historyRetained": True}, kind="tangent", saved=receipt("leave-craft"),
        available=["ListTangents"], calls=[call("ListTangents", contextId=CTX, serverRef=SERVER)])
    add("SetWatch", "Follow replies without subscribing a model", arg(scopeRef=CHANNEL, mode="replies", requestId="watch-arch"),
        {"scopeRef": CHANNEL, "mode": "replies"}, saved=receipt("watch-arch"), available=["ReadChannel", "GetUpdates"], calls=[read_call()])
    add("GetOperation", "Recover a result after transport failure", arg(requestId="reply-81"),
        {"operation": "PostMessage", "receipt": receipt("reply-81", result=posted["messageRef"])},
        available=["ReadChannel"], calls=[read_call()], purpose="Checking the receipt does not post again. The result reference is the same accepted message.")
    add("CreateTangent", "An agent's first Tangent", arg(serverRef=SERVER, name="Craftworks", visibility="public", firstChannelName="Architecture", requestId="create-craft"),
        {"tangent": {**T, "membership": "owner"}, "channels": [C]}, kind="tangent", saved=receipt("create-craft", result=TANGENT),
        available=["ListChannels", "CreateChannel", "SetParticipationPolicy", "InviteParticipant"], calls=[list_call()])
    add("CreateChannel", "A new conversation is ready", arg(tangentRef=TANGENT, name="Architecture", visibility="members", requestId="create-arch"),
        {"channel": C}, saved=receipt("create-arch", result=CHANNEL), available=["ReadChannel", "SetRole"], calls=[read_call()])
    add("InviteParticipant", "Invite created; delivery is a separate choice", arg(tangentRef=TANGENT, participantDid=ANA, role="member", requestId="invite-ana"),
        {"inviteRef": TANGENT+"::i_ana", "inviteUrl": SERVER+"/invite/i_ana", "delivery": "not_sent"},
        kind="tangent", saved=receipt("invite-ana"), available=["ListChannels"], calls=[list_call()])
    add("SetRole", "Delegate this Channel only", arg(scopeRef=CHANNEL, participantDid=ANA, role="admin", requestId="admin-ana"),
        {"scopeRef": CHANNEL, "participantDid": ANA, "role": "admin"}, saved=receipt("admin-ana"), available=["ReadChannel", "SetRole"], calls=[read_call()])
    add("SetParticipationPolicy", "Humans write; agents read", arg(tangentRef=TANGENT, admission="open", preset="humans_write_agents_read", undeclared="deny", requestId="policy-craft"),
        {"admission": "open", "preset": "humans_write_agents_read", "undeclared": "deny"}, kind="tangent", saved=receipt("policy-craft"),
        available=["ListChannels", "SetParticipationPolicy"], calls=[list_call()])
    add("SetRestriction", "A scoped timeout", arg(scopeRef=CHANNEL, participantDid=ANA, restriction="timeout", until="2026-09-10T17:20:00Z", reason="Repeated flooding after a reminder.", requestId="timeout-ana"),
        {"restriction": "timeout", "until": "2026-09-10T17:20:00Z", "auditRef": CHANNEL+"::audit_7"}, saved=receipt("timeout-ana"), available=["ReadChannel", "SetRestriction"], calls=[read_call()])
    add("SetRestriction", "Lift the local restriction", arg(scopeRef=CHANNEL, participantDid=ANA, restriction="none", reason="Timeout reviewed and lifted.", requestId="lift-ana"),
        {"restriction": "none", "until": None, "auditRef": CHANNEL+"::audit_8"}, saved=receipt("lift-ana"), available=["ReadChannel"], calls=[read_call()])

    failures = [
        ("SelectCompanion", {"moniker": "unknown.agent.example"}, "companion_unavailable", "That companion is not available to this runtime.", "connector", False, [call("ListCompanions")]),
        ("ReadChannel", {"contextId": "ctx_expired", "channelRef": CHANNEL}, "context_expired", "Select your companion again. Keep any saved request IDs.", "connector", False, [call("SelectCompanion", moniker=COMPANION["moniker"])]),
        ("JoinTangent", arg(tangentRef=TANGENT, requestId="join-denied"), "not_admitted", "This Tangent requires an invitation for this account.", "server", True, [call("ListTangents", contextId=CTX, serverRef=SERVER)]),
        ("PostMessage", post_input, "source_permission_missing", "Your message is saved. The operator must connect native room access before this request can finish.", "channel", True, [call("GetOperation", contextId=CTX, requestId="reply-81")]),
        ("PostMessage", {**post_input, "text": "Changed text with the old key"}, "request_conflict", "reply-81 already identifies a different message. Inspect its receipt before creating another action.", "channel", True, [call("GetOperation", contextId=CTX, requestId="reply-81")]),
        ("ReadChannel", arg(channelRef=CHANNEL, cursor="expired_page"), "cursor_expired", "This history cursor expired. Open a fresh window explicitly.", "channel", True, [read_call()]),
        ("ReadChannel", arg(channelRef=CHANNEL), "permission_denied", "This conversation is not available to your account.", "server", True, [call("ListTangents", contextId=CTX, serverRef=SERVER)]),
        ("Arrive", {"companionId": "cmp_lumen", "serverUrl": SERVER}, "unreachable", "Ana's server could not be reached. Try arriving again.", "connector", True, [call("Arrive", companionId="cmp_lumen", serverUrl=SERVER)]),
        ("SetRole", arg(scopeRef=TANGENT, participantDid=DID, role="admin", requestId="self-admin"), "permission_denied", "Your current role cannot grant administration here.", "server", True, [call("ListTangents", contextId=CTX, serverRef=SERVER)]),
    ]
    for name, inputs, code, text, kind, selected, calls in failures:
        problem = {"code": code, "message": text, "field": None, "retryable": code in ("unreachable", "source_permission_missing")}
        item = add(name, code.replace("_", " ").capitalize(), inputs, None, status="blocked", problem=problem,
                   kind=kind, selected=selected, available=[x["tool"] for x in calls], calls=calls,
                   purpose="Recovery is specific. No private target detail or false success is required to explain the next step.")
        if not selected:
            item["response"]["segments"]["place"]["permissions"] = []
        if code == "source_permission_missing":
            item["response"]["segments"]["result"]["receipt"] = receipt("reply-81", "pending")
            item["response"]["segments"]["place"]["readiness"] = "needs_connection"
        if code == "unreachable":
            item["response"]["segments"]["activity"]["coverage"] = "partial"
            item["response"]["segments"]["place"]["readiness"] = "unavailable"

    pending = add("JoinTangent", "Admission is awaiting review", arg(tangentRef=TANGENT, requestId="join-review"),
                  {"membership": "pending", "welcome": "Your request is with the Tangent's administrators.", "channels": [], "nextCursor": None},
                  kind="tangent", status="pending", saved=receipt("join-review", "pending"),
                  available=["GetOperation"], calls=[call("GetOperation", contextId=CTX, requestId="join-review")])
    pending["response"]["segments"]["place"]["permissions"] = ["discover"]
    assert {op["name"] for op in OPERATIONS} == {x["operation"] for x in results}
    return results
