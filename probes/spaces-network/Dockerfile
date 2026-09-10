FROM node:24-bookworm-slim@sha256:ba849c60be29959425b8734d57b8b4b7d56f98edd9504c9af091d5281095a71e
RUN apt-get update && apt-get install -y --no-install-recommends python3 make g++ ca-certificates git && rm -rf /var/lib/apt/lists/*
RUN npm install -g pnpm@11.11.0
WORKDIR /atproto
COPY . .
ENV PUPPETEER_SKIP_DOWNLOAD=true
RUN pnpm install --frozen-lockfile
# Git for Windows may check this upstream symlink out as a text file.
RUN ln -sf ../hydration-data.d.ts packages/oauth/oauth-provider-ui/src/hydration-data.d.ts
RUN pnpm prebuild
RUN pnpm exec tsc --build packages/dev-env/tsconfig.build.json
RUN pnpm --filter @atproto/oauth-provider-ui build:ui
ENV LOG_ENABLED=false
EXPOSE 2582 2583 2584 2585
CMD ["node", "--import", "/atproto/tangent-probe/patch-varint.mjs", "/atproto/tangent-probe/network.mjs"]
