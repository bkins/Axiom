# Offline Mermaid bundle

The committed runtime is self-contained. The application never uses npm or a CDN.
The manifest identifies Mermaid 11.16.1 and the runtime SHA-256. The dependency
lock pins every dependency, including esbuild 0.25.12. THIRD-PARTY-NOTICES.txt
contains licenses for bundled packages; Mermaid is MIT licensed.

To reproduce from the repository root, create artifacts/mermaid-vendor-build,
copy this directory's package-lock.json there, and create package.json from the
lock's packages[""] entry. Run npm ci --prefix artifacts/mermaid-vendor-build
--ignore-scripts --no-audit --no-fund, followed by node scripts/Build-Mermaid.mjs.
The builder refuses external imports and regenerates the bundle and notices.

HTML labels, document configuration and active/external SVG content are disabled.
Source fallbacks are deliberate and documented in the maintained User Guide.
