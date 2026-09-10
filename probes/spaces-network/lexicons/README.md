# Local conversation vocabulary

These declarations belong to the disposable test resolver. `local.tangent` is not a registered production namespace. The startup harness publishes both schemas through its actual Lexicon authority account. The room is a Space type accepting the message collection; room titles, topics and governance remain local Tangent metadata.

A message contains text, a source timestamp and an optional reply reference to the exact source URI/CID. Authorship comes from the verified repository DID, never from a record field. A Spaces record URI includes the authority, Space type/key and author DID; the ordinary repository AT-URI formatter does not describe this path. Tangent checks reply existence, version and room during source acceptance.

Tangent additionally rejects unsupported fields, blank/null-containing text and bodies exceeding 4096 UTF-8 bytes. The pinned PDS does not dynamically enforce custom record schemas: both Tangent writes and ingestion validate content. The official Lexicon validator is exercised separately by `../inspect-message-schema.mjs`.

The original running network published the base text/timestamp schema before reply handling was added. Existing evidence proves signed resolution of that base schema and real reply acceptance by Tangent. The files here explicitly declare replies and are published on the next fresh network launch; the current network was preserved for recovery and demo continuity. No claim is made that the PDS validated those replies dynamically.
