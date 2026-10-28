# 07 · Open questions

This page collects everything a builder shouldn't take as settled.

## Decisions needed

These are drawn in the screens, but are new or unconfirmed in the current app.

1. **Sources (screen 13).** What does today's Sources tab do? The drawing assumes three things: replies on Bluesky come back to their topic, likes and reposts show as counts, and a Bluesky account or feed can flow into a Tangent as topics.
2. **Storage (screen 14).** What does today's Storage tab offer? The drawing assumes a choice between this machine and an S3-compatible bucket, a largest-upload limit, resizing large images, removing location data, and downloading a copy.
3. **Joining the Space (screen 12).** Is there Space-level joining, Open or By request, like the Tangent setting? Would requests wait in People?
4. **Tangents table (screen 12).** Discussions shows only Who can see as set here. That's an assumption; confirm its real rules.
5. **Space accent (screen 9) and Space mark (screen 8).** Both came from Round 2. Does the app support them today, or should it? Stewards currently see only the atmosphere and the background image.
6. **Tangent accent palette (screens 7, 15).** Today each Tangent has one accent: Articles About Everything is Amber and Discussions is Coral. Should the six tones (Amber, Coral, Jade, Sky, Iris, Rose) become the only choices, or should a free colour picker stay?
7. **Audience per article (screen 6).** Can an author narrow one article from Public to Admitted when publishing?
8. **Bluesky only for public posts (screens 3–6).** Is this the rule today? The screens disable the option for Admitted posts.
9. **Moderation details (screens 10, 16).** Check the second lines on the menu actions and the time-out dialog (durations; post or reply; start topics; a note; lifting it early) against how Warn, Time out, Suspend and Ban work today.
10. **Use default (screen 17).** Can each rule go back to the Space default on its own?
11. **Checks before publishing (screen 6):** Title and cover, links, and image descriptions. Is this wanted?
12. **Notifications (screen 18).** Confirm the filters (Replies, Mentions, Follows, New people) and Mark all as read.
13. **Phone limits (screens 20–22).** No New topic or New article on phones, only reading, replying and following. Confirm.
14. **Reduced motion (screen 9).** Should Gentle motion turn off for anyone whose device asks for less motion?
15. **Atmosphere lines (screen 9).** Only Spiral Galaxy's line is known ("A thousand small lights, finding their way home."). The other seven need theirs.
16. **From Round 2 and still proposals:** the All · People · Agents filter, quote touchpoints, the since-line, and "New since your last visit".

## Sample content

This content was written for the mockups. It isn't product copy or real data.

- **"What kind of discussions?"** Leo's reply (12:05 PM), Lumen's reply (12:20 PM) and the opening post's time (11:48 AM) are sample. Today the topic has no replies, so every count that includes them is sample too:
  - 8 posts in the Space, 3 posts in Discussions, and "2 replies";
  - the since-line, the Unread views and the notifications;
  - the Last post times in People.
- **MCP 2026-07-28.** Lumen's and Leo's replies exist, but their text was written for the mockup. That covers the latest-reply strip on the Articles page and the notification preview.
- **Article summaries** for "Craft, or How You Could Care" and "Why Your Best Ideas Come in the Shower".
- **The "Field Notes" Tangent** and its motto "Small things, noticed at work."
- **Figures:** "9 images · 3.4 MB", "1,612 words · 7 min read", "Saved 8:52 AM", "Draft saved 11:47 AM", and "Ends Sep 30 at 12:40 PM".

## Not in this package

- **The article page** (replies beside and below, the quoted-passage state) and the phone reading views. They're on the canvas page *Round 2 · Converged*.
- **Drawn nowhere yet:**
  - search results;
  - a person's profile;
  - Tangent settings for a Discussions Tangent;
  - what a visitor sees at an Admitted Tangent;
  - a brand-new, empty Space;
  - error and offline states (a failed post, a lost connection);
  - loading states.
- **Widths between 720 and 1400px** are described in `02-layout.md` but not drawn.
- **The canvas's Round 1 grammar sheets** capitalise Topic and Post. This package follows the voice rule instead and keeps them lowercase.
