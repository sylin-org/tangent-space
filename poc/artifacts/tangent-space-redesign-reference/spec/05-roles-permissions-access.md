# 05 · Roles, permissions and access

This section maps the current app's model onto the redesign. **Behaviour doesn't change.** Names, descriptions, grouping and layout do. Anything marked *proposal* is new and needs a decision.

## Roles

These are the four roles the app has today.

| Role | Dot | Built in | Members today | Description in the redesign |
|---|---|---|---|---|
| Administrators | red (`--ts-role-red`) | no | none | Trusted stewards who can manage Tangents, topics and posts across the Space. |
| Member | violet (`--ts-role-violet`) | no | none | A member of the Space. Each Tangent's access list decides where they can act. |
| Owner | gold (`--ts-role-gold`) | yes | Leo Botinelly (automatic) | The accountable person who owns the Space. |
| Participant | teal (`--ts-role-teal`) | yes | everyone signed in (2) | Everyone signed in holds this role. It's never given or taken away. |

- Show real counts in each row instead of "Auto": "No one yet", "Leo Botinelly · automatic", "Everyone signed in · 2". Counts are for people given a role directly; say so once under the list.
- **Steward** is not a role. It's the word for the Owner, plus anyone whose role lets them manage.
- The Roles screen puts the list and the selected role side by side. The role has three tabs: Details, Permissions and Members.

## Permission catalog

The left column is today's label. The right column is the redesign label and description. The last column is the current state for **Administrators**, taken from the screenshot. Screen 11 shows these states, plus *Ban participants* switched on as an unsaved change so the save bar is visible.

| Group | Today | Redesign | Description | Administrators today |
|---|---|---|---|---|
| **Space** (was "Server") | Enter server | Enter the Space | See this Space even when its access list leaves them out. | on |
| | Manage server | Manage the Space | Change the Space's identity, how it runs, and its access defaults. | off |
| **Tangents** | Create Tangents | Create Tangents | Create a Tangent anywhere in the Space. | on |
| | See every Tangent | See every Tangent | See Tangents whatever their access lists say. | on |
| | Manage every Tangent | Manage every Tangent | Change details and access for every Tangent. | on |
| **Topics** | Create Topics anywhere | Create topics anywhere | Start a topic in every Tangent they can see. | on |
| | See every Topic | See every topic | See topics whatever their access lists say. | on |
| | Manage every Topic | Manage every topic | Change details and access for every topic. | on |
| **Posts** | Post anywhere | Post anywhere | Reply in every topic they can see. | on |
| | Remove any Post | Remove any post | Moderate posts in every topic they can see. | on |
| **People and roles** (was "Other") | Manage roles | Manage roles | Create roles, change their permissions and manage their members. | off |
| | Manage participants | Manage participants | Approve, suspend and restore participants. | on |
| | Warn participants | Warn participants | Send an official warning that stays on their record. | on |
| | Time out participants | Time out participants | Restrict someone in a Tangent for a set time. Reversible. | on |
| | Ban participants | Ban participants | Ban participants from a Tangent. | off |

Each group header shows how many permissions are on ("2 of 3 on"). A search field ("Find a permission") filters rows as you type.

## Access rules

### The model

- Access is set per level, and flows down: Space → Tangent → topic. The topic level follows from "Manage every topic: change details **and access** for every topic".
- At each level, a rule is either **Set here** or **Inherited**.
- A set rule can go back to the level above with **Use default** ("Follow the Space default instead").
- A rule lists the roles added at that level. When nothing is added, it shows **Stewards only**, the redesign's label for today's "Only global stewards".
- Some people can always act, whatever the list says. Every rule says who: the Owner, Administrators, and any role holding the matching Space-wide permission.

### Tangent rules (screen 17)

| Rule | Description | Always allowed |
|---|---|---|
| Who can see | Who can find and open this Tangent. | Owner, Administrators, and roles with See every Tangent |
| Who can post | Who can reply in its topics. | Owner, Administrators, and roles with Post anywhere |
| Who can create topics | Who can start a new conversation here. | Owner, Administrators, and roles with Create topics anywhere |
| Who can manage | Who can change this Tangent's details and access. | Owner, Administrators, and roles with Manage every Tangent |

- Roles are added with a picker, not by typing "@role". Roles that are always allowed show in the picker as disabled, with the reason.
- **Joining** (per Tangent) is **Open** ("People join right away.") or **By request** ("A steward approves each person."). "Joining gives people this Tangent's history, within its reading rules."
- **Narrowing who can read** asks what happens to existing posts before anything changes. This is the current app's behaviour, and the wording is updated.
- In today's app, *Articles About Everything* has all four rules set here with no roles added (screen 17 shows this).

### Space rules (screen 12, partly proposal)

- **Who can create Tangents:** set here. Always allowed: Owner, Administrators, and roles with Create Tangents. The current app keeps this in Access ("Who may create Tangents is configured in Access").
- **Agents can own Tangents:** a switch in General, off today ("Allow agents to own Tangents").
- *Proposal:* **Joining the Space**, Open or By request, mirroring the Tangent setting. Requests would wait in People.
- *Proposal:* a **Tangents** table that shows, for each Tangent, which of the four rules are Set here or Inherited, with an Edit link.

### Audience on content

- **Public:** anyone on the web can read it. **Admitted:** readable by the people admitted to this Tangent.
- A Tangent's audience shows in its masthead and in every composer ("· Public" or "· Admitted").
- *Proposal:* when publishing an article, the author can narrow a Public Tangent's article to Admitted (the Publish panel on screen 6).
- *Proposal:* only public posts can go to Bluesky. For Admitted posts the option is disabled, with "Only public posts can go to Bluesky".

## Moderation

The People menu (screen 10) and the Tangent People page (screen 16) turn permissions into actions.

| Action | Permission | Scope | What the person sees |
|---|---|---|---|
| Change roles | Manage roles | Space | The roles submenu, showing the current value ("No roles") |
| Warn… | Warn participants | Space | "An official warning, kept on their record" |
| Time out… | Time out participants | One Tangent | A dialog: how long (1 hour, 1 day, 1 week, Custom), what they can't do (post or reply; start topics), an optional note to them, and the end time ("Ends Sep 30 at 12:40 PM. You can lift it sooner."). They can still read. |
| Suspend | Manage participants | Space | "Can't post anywhere until you restore them" |
| Ban from a Tangent… | Ban participants | One Tangent | Shown in `danger`: "They can't go back to it" |

The action labels come straight from the permission names. The dialog's details (the durations, what's restricted, the note) are a *proposal* that needs checking against how time outs work today.
