# Static response security headers

This separate, unpublished five-file Koan correction moves the existing security-header middleware before
default/static-file serving. Static HTML previously terminated the pipeline before receiving `X-Frame-Options`,
`Referrer-Policy`, `X-Content-Type-Options`, or a configured CSP. Controllers already received those headers.

The base is `e07a84cc3f71a0867f1122b03b723cc80727e772`. Tangent layers this correction after the separate
`koan-atproto-auth` contribution. Its 49 postimages are validated before this
unchanged header patch applies; this package has no overlapping postimages. No new
option, app middleware, public API, package version, or upstream publication is introduced.

Create a fresh review checkout containing both contributions:

```powershell
./contributions/koan-static-headers/verify.ps1 -Destination C:/work/koan-headers-review -RunTests
```

Use `-SourceRepository C:/work/koan-source` to obtain the pinned committed base from an existing local repository
without changing that repository. On an existing checkout with the exact auth contribution already applied:

```powershell
./contributions/koan-static-headers/apply.ps1 -Checkout .local/upstream/koan-framework -RunTests
```

The apply command validates both manifests and all 49 required auth postimages, checks the five header postimages,
and applies the patch only when needed and `git apply --check` succeeds. It preserves unrelated edits and refuses
a mismatched base or conflicting patch. It never resets, cleans, or deletes a checkout. The manifest records each
resulting Git blob and the patch SHA-256. Tests run only when `-RunTests` is supplied.

The focused HTTP spec uses a real `AddKoan()` TestServer with a physical `index.html`. Its three cases cover
enabled headers, explicitly disabled headers, and proxied configuration across `/`, `/index.html`, and the
framework `/health/live` controller. The enabled case failed before the correction; all three passed afterward.
`verification.json` records the fresh-checkout result and Tangent build separately. This is a narrow pipeline
regression proof, not a broader security certification.
