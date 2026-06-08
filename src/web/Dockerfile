FROM node:24-alpine AS build
# Pinned to the version that produced pnpm-lock.yaml — newer pnpm ships a
# minimumReleaseAge supply-chain policy that rejects freshly-published packages.
RUN npm install -g pnpm@10.24.0
WORKDIR /src
COPY package.json pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY . .
# Vite/esbuild strips types without checking them, so typecheck explicitly —
# the image must not build with type errors the CI gate would reject.
RUN pnpm typecheck
RUN pnpm build

FROM caddy:2-alpine
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /src/build/client /srv
