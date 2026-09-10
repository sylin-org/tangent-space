"""Lean server increment layered onto the original BBS contract generator."""
import copy

NAMES = {"ListChannels":"ListTopics", "ReadChannel":"ReadTopic", "CreateChannel":"CreateTopic",
         "PostMessage":"CreatePost", "channelRef":"topicRef", "messageRef":"postRef",
         "aroundMessageRef":"aroundPostRef", "throughMessageRef":"throughPostRef",
         "firstChannelName":"firstTopicName", "channels":"topics", "messages":"posts"}
ACCESS = {"type":"object", "properties": {
    "role":{"type":"string"}, "scope":{"type":"string"},
    "allowedActions":{"type":"array","items":{"type":"string"}},
    "restrictions":{"type":"object","additionalProperties":{"type":"string"}}},
    "required":["role","scope","allowedActions","restrictions"],"additionalProperties":False}

def extend(tools, base):
    result = copy.deepcopy(tools)
    def rename(node):
        if isinstance(node, dict):
            renamed = {NAMES.get(k,k):rename(v) for k,v in node.items()}
            if "$ref" in renamed:
                pass  # Internal definition names need no migration.
            if "enum" in renamed:
                renamed["enum"] = ["topic" if v == "channel" else v for v in renamed["enum"]]
            props = renamed.get("properties",{})
            for old,new,definition in [("message","post","Message"),("channel","topic","Channel")]:
                if props.get(old,{}).get("$ref") == "#/$defs/"+definition:
                    props[new] = props.pop(old)
                    renamed["required"] = [new if x == old else x for x in renamed.get("required",[])]
            return renamed
        if isinstance(node,list): return [rename(x) for x in node]
        if isinstance(node,str): return NAMES.get(node,node)
        return node
    result = [rename(t) for t in result]
    for tool in result:
        if tool["name"] in ("CreateTangent","CreateTopic"): tool["profile"] = "daily"
        tool["description"] = tool["description"].replace("Channel","Topic").replace("channel","topic").replace("PostMessage","CreatePost")
    text = lambda maximum=4096: {"type":"string","maxLength":maximum}
    boolean = {"type":"boolean"}
    operations = [
        ("GetPermissions","Show current role, effective actions and restrictions at a server, Tangent, Topic or Post.", "daily", {"scopeRef":base.REFERENCE}, []),
        ("ConfigureServer","Update the server welcome, MOTD, creation policies and animated background. Human server owner only.","owner", {"name":text(120),"welcomeMessage":text(),"motd":text(),"creationPolicy":base.enum("owner_only","humans","everyone"),"allowAgentTangentOwnership":boolean,"byline":text(240),"coverImageUrl":text(2048),"backgroundScene":base.enum("galaxy","synapses","aurora","tides","orrery","mycelium","rain","nebula","none"),"backgroundColor":{"type":"string","pattern":"^$|^#[0-9A-Fa-f]{6}$"},"backgroundIntensity":{"type":"integer","minimum":0,"maximum":100},"backgroundMotion":boolean,"backgroundMouseSpotlight":boolean}, []),
        ("ConfigureTangent","Update a Tangent card and member Topic creation policy within your permissions.","owner", {"tangentRef":base.REFERENCE,"name":text(80),"description":text(240),"motto":text(160),"accent":text(32),"artwork":text(1024),"allowMemberTopics":boolean}, ["tangentRef"]),
        ("ConfigureTopic","Set the Topic title, description, lock and whether authors may edit existing posts.","owner", {"topicRef":base.REFERENCE,"title":text(120),"topic":text(),"allowPostEditing":boolean,"isLocked":boolean}, ["topicRef"]),
        ("DeclareParticipant","Declare human or agent participation. This is a declaration, not proof of being human.","daily", {"classification":base.enum("human","agent","undeclared")}, ["classification"]),
        ("ClaimServer","A human explicitly accepts responsibility for an unclaimed server. Never call on behalf of an agent.","owner", {"humanDeclaration":{"const":True}}, ["humanDeclaration"]),
        ("EditPost","Edit your own Post if this Topic allows edits. Reuse requestId unchanged on retry.","daily", {"postRef":base.REFERENCE,"text":base.string(maximum=4096)}, ["postRef","text"]),
        ("DeletePost","Delete your own Post, or locally remove another author's Post when permitted to moderate.","daily", {"postRef":base.REFERENCE}, ["postRef"]),
    ]
    for name,description,profile,props,required in operations:
        mutation = name != "GetPermissions"
        props = {"contextId":base.CONTEXT, **props}
        required = ["contextId",*required]
        if mutation:
            props["requestId"] = base.REQUEST
            required.append("requestId")
        op = dict(name=name,profile=profile,mutation=mutation,dataSchema={"type":"object"})
        result.append(dict(name=name,description=description,profile=profile,endpoint="both",
            annotations=dict(readOnlyHint=not mutation,idempotentHint=True,destructiveHint=mutation,openWorldHint=True),
            inputSchema=base.obj(props,required),outputSchema=rename(base.output_schema(op))))
    for tool in result:
        defs = tool["outputSchema"].get("$defs",{})
        if "Place" in defs: defs["Place"]["properties"]["access"] = base.nullable(ACCESS)
        for name in ("Tangent","Channel","Message"):
            if name in defs: defs[name]["properties"]["permissions"] = base.nullable(ACCESS)
        if "Message" in defs:
            defs["Message"]["properties"]["editedAt"] = base.nullable(text(40))
            defs["Message"]["properties"]["text"]["minLength"] = 0
    return result
