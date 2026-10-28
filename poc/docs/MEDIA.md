# Post media

Tangent stores uploaded Post images in the configured local store or private S3-compatible bucket. A `PostMediaAsset` entity records the uploader, Tangent, optional Topic, file type, size, checksum and owning Post. Full-state backups include both metadata and file bytes. Community artwork remains a separate, deliberately public resource.

## Upload and publication

`POST /api/media` accepts JSON `{ tangentKey, topicKey?, fileName, content }`, where `content` is base64-encoded image bytes. The caller needs the `post` transport grant and current permission to create a Topic in that Tangent or reply in the selected Topic. Cookie requests must originate from the same host. The response includes `{ id, url, fileName, contentType, size }`.

Uploads accept PNG, JPEG and WebP under the configured 1–20 MiB limit. The decoder bounds pixel count. When resizing or metadata removal is enabled, processing corrects orientation and re-encodes to WebP without source metadata. Resizing limits the largest dimension to 2400 pixels. With both options off, valid source bytes are retained. Each participant may retain at most 40 unpublished uploads; each Post may reference up to 20 owned images. The draft query is bounded and indexed. Files are written atomically, and failed metadata saves remove the unpublished file.

An upload is private to its author until a Markdown Post adopts its `/media/{id}` URL. A file belongs to one Post; upload another copy to use an image in another Post. Publication validates ownership and scope before accepting the Post, then binds its media. An exact Post retry repairs binding after a partial write. The original image URL remains stable through edits.

## Reading and privacy

`GET /media/{id}` checks the owning Post and its current Topic/Tangent audience on every request. Anonymous readers can retrieve an image only while its Post is publicly readable and still includes it. Images removed from a Post are available only to participants authorized to read that Post's edit history. Missing and inaccessible resources both return 404.

Responses use `private,no-store`, `Vary: Cookie, Authorization`, and `X-Content-Type-Options: nosniff`. Images are not served from a blanket public static-files directory, and changing a conversation's audience also changes access to its media. Previously downloaded public bytes cannot be recalled.

The visual editor uploads selected, pasted or dropped images and inserts the returned URL into canonical Markdown. It retains the image description, prevents submission during upload, and abandons a pending upload when the editing context changes. External image URLs remain supported; rendering does not fetch remote URLs on the server.

## Storage controls

Owner-only `GET/PATCH /api/media/storage` manages upload policy and the destination for new files. S3 configuration uses a root HTTPS endpoint, bucket and protected credentials. Saving a destination tests a private write/read/delete before persisting it. Existing assets keep a store identifier and continue through their original store; changing the destination does not move them. Local full-state backups include local bytes and the protected S3 configuration, but an external bucket needs its own backup policy.

`GET /api/media/storage/download` streams an owner-authorized ZIP in bounded metadata pages, with images and metadata but no credentials. Temporary ZIP storage is removed when the response closes. The archive is a media copy; it is not a complete installation backup. Community logos and artwork remain separately public and use the 5 MiB artwork upload limit.
