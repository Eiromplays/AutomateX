// Curated starter workflows. "Use template" navigates to /workflows/new with this as
// the importDoc, so it flows through the same review screen as a file import — the user
// tweaks URLs / connection refs / rooms before creating. Connection refs and non-cron
// triggers (rss, etc.) are noted in the description for the user to wire up after.

type TemplateDoc = {
  automatex: number;
  name: string;
  description: string;
  continueOnFailure?: boolean;
  steps: { actionType: string; name?: string; config: Record<string, unknown> }[];
  edges?: { from: number; to: number; label: string | null }[];
  triggers?: { type: string; config: Record<string, unknown> }[];
};

export type WorkflowTemplate = {
  id: string;
  name: string;
  description: string;
  category: string;
  doc: TemplateDoc;
};

export const templates: WorkflowTemplate[] = [
  {
    id: "rss-notify",
    name: "RSS → Discord alert",
    description:
      "Ping Discord for each new item in any RSS/Atom feed. After creating, add an 'rss' trigger pointing at your feed, and a 'discord' connection.",
    category: "Alerts",
    doc: {
      automatex: 1,
      name: "rss-alert",
      description: "Notify on new feed items.",
      steps: [
        {
          actionType: "discord.send",
          name: "Notify",
          config: {
            webhookUrl: "{{connections.discord.webhookUrl}}",
            content: "📰 {{trigger.payload.title}}\n{{trigger.payload.link}}",
          },
        },
      ],
    },
  },
  {
    id: "daily-digest",
    name: "Daily digest (9am)",
    description:
      "Runs every morning at 09:00, fetches a URL, and posts it to Matrix. Edit the URL, room id, and point accessToken at a 'matrix' connection.",
    category: "Scheduled",
    doc: {
      automatex: 1,
      name: "daily-digest",
      description: "A scheduled morning digest.",
      steps: [
        {
          actionType: "http.request",
          name: "Fetch",
          config: { method: "GET", url: "https://api.github.com/zen", headers: { Accept: "application/json" } },
        },
        {
          actionType: "matrix.send",
          name: "Post digest",
          config: {
            homeserverUrl: "https://matrix-client.matrix.org",
            accessToken: "{{connections.matrix.accessToken}}",
            roomId: "!REPLACE-ME:matrix.org",
            msgType: "m.notice",
            message: "🌅 Daily digest: {{steps.0.output.body}}",
          },
        },
      ],
      triggers: [{ type: "cron", config: { cron: "0 9 * * *" } }],
    },
  },
  {
    id: "uptime-alert",
    name: "Website uptime alert",
    description:
      "Checks a URL every 5 minutes and pushes a Pushover alert only when it is NOT returning 200. Edit the URL and add a 'pushover' connection.",
    category: "Monitoring",
    doc: {
      automatex: 1,
      name: "uptime-alert",
      description: "Alert when a site is down.",
      steps: [
        {
          actionType: "http.request",
          name: "Check",
          config: { method: "GET", url: "https://example.com", failOnErrorStatus: false },
        },
        {
          actionType: "gate",
          name: "Only when down",
          config: { value: "{{steps.0.output.statusCode}}", notEquals: "200" },
        },
        {
          actionType: "pushover.send",
          name: "Alert",
          config: {
            appToken: "{{connections.pushover.appToken}}",
            userKey: "{{connections.pushover.userKey}}",
            title: "Site down",
            message: "⚠ example.com returned HTTP {{steps.0.output.statusCode}}",
          },
        },
      ],
      triggers: [{ type: "cron", config: { cron: "*/5 * * * *" } }],
    },
  },
  {
    id: "ai-summary",
    name: "AI page summary",
    description:
      "Fetches a page and summarizes it with a local LLM (Ollama), then notifies Discord. Point baseUrl/model at your LLM and add a 'discord' connection.",
    category: "AI",
    doc: {
      automatex: 1,
      name: "ai-summary",
      description: "Summarize a page with an LLM.",
      steps: [
        {
          actionType: "http.request",
          name: "Fetch",
          config: { method: "GET", url: "https://api.github.com/zen", headers: { Accept: "application/json" } },
        },
        {
          actionType: "llm.prompt",
          name: "Summarize",
          config: {
            baseUrl: "http://localhost:11434",
            model: "qwen2.5:3b",
            system: "Summarize in one short sentence, no preamble.",
            prompt: "Summarize: {{steps.0.output.body}}",
          },
        },
        {
          actionType: "discord.send",
          name: "Notify",
          config: { webhookUrl: "{{connections.discord.webhookUrl}}", content: "🤖 {{steps.1.output.text}}" },
        },
      ],
    },
  },
  {
    id: "branching-watchdog",
    name: "Branching uptime watchdog",
    description:
      "Showcases branching: probe a URL, switch on the status, and on failure fan out into two parallel lanes (page + log) that rejoin — with continue-on-failure so the page fires even if logging breaks. Add a 'pushover' connection and edit the URL.",
    category: "Monitoring",
    doc: {
      automatex: 1,
      name: "branching-watchdog",
      description: "Parallel alert lanes on failure, joined at the end.",
      continueOnFailure: true,
      steps: [
        { actionType: "http.request", name: "Probe", config: { method: "GET", url: "https://example.com", failOnErrorStatus: false } },
        { actionType: "switch", name: "Healthy?", config: { value: "{{steps.0.output.statusCode}}", cases: [{ label: "up", equals: "200" }] } },
        {
          actionType: "pushover.send",
          name: "Heartbeat OK",
          config: { appToken: "{{connections.pushover.appToken}}", userKey: "{{connections.pushover.userKey}}", title: "API healthy", message: "Returned {{steps.0.output.statusCode}}.", priority: -1 },
        },
        {
          actionType: "http.request",
          name: "Diagnose",
          config: { method: "POST", url: "https://httpbin.org/post", contentType: "application/json", body: "{\"status\":\"{{steps.0.output.statusCode}}\"}" },
        },
        {
          actionType: "pushover.send",
          name: "Page on-call",
          config: { appToken: "{{connections.pushover.appToken}}", userKey: "{{connections.pushover.userKey}}", title: "API DOWN ({{steps.0.output.statusCode}})", message: "Probe failed — paging.", priority: 1 },
        },
        {
          actionType: "http.request",
          name: "Log incident",
          config: { method: "POST", url: "https://httpbin.org/post", contentType: "application/json", body: "{\"status\":\"{{steps.0.output.statusCode}}\"}", failOnErrorStatus: true },
        },
        {
          actionType: "pushover.send",
          name: "Incident recorded",
          config: { appToken: "{{connections.pushover.appToken}}", userKey: "{{connections.pushover.userKey}}", title: "Incident logged", message: "Paged and logged for {{steps.0.output.statusCode}}.", priority: 0 },
        },
      ],
      edges: [
        { from: 0, to: 1, label: null },
        { from: 1, to: 2, label: "up" },
        { from: 1, to: 3, label: "default" },
        { from: 3, to: 4, label: null },
        { from: 3, to: 5, label: null },
        { from: 4, to: 6, label: null },
        { from: 5, to: 6, label: null },
      ],
    },
  },
  {
    id: "nightly-backup",
    name: "Nightly database backup",
    description:
      "Dumps the AutomateX Postgres DB on the host every night and deletes dumps older than 14 days. Add an 'ssh' connection (host, username, privateKey) to the deploy host; adjust the compose path and retention. See docs/recipes/backups.md.",
    category: "Maintenance",
    doc: {
      automatex: 1,
      name: "nightly-backup",
      description: "pg_dump + gzip on the host, nightly, with rotation.",
      steps: [
        {
          actionType: "ssh.command",
          name: "Dump + rotate",
          config: {
            host: "{{connections.ssh.host}}",
            username: "{{connections.ssh.username}}",
            privateKey: "{{connections.ssh.privateKey}}",
            command:
              "mkdir -p ~/automatex/backups && " +
              "docker compose -f ~/automatex/docker-compose.prod.yml exec -T postgres " +
              "pg_dump -U automatex automatex | gzip > ~/automatex/backups/automatex-$(date +%F).sql.gz && " +
              "find ~/automatex/backups -name 'automatex-*.sql.gz' -mtime +14 -delete",
          },
        },
      ],
      triggers: [{ type: "cron", config: { cron: "0 3 * * *" } }],
    },
  },
  {
    id: "self-deploy",
    name: "Self-deploy on new release",
    description:
      "Polls GitHub for new AutomateX releases and runs `docker compose pull && up -d` on the host over SSH, then notifies. Dedups per release tag (kv.setIfAbsent) so an incidental feed change can't redeploy the same version. Needs the Feed plugin (http.poll trigger), an 'ssh' connection, and Pushover — edit host/username/paths. Pull model: no public webhook needed. See docs/recipes/self-deploy.md.",
    category: "Maintenance",
    doc: {
      automatex: 1,
      name: "self-deploy",
      description: "Auto-update when a new release is published.",
      steps: [
        {
          actionType: "gate",
          name: "Only on HTTP 200",
          config: { value: "{{trigger.payload.statusCode}}", equals: "200" },
        },
        {
          actionType: "kv.setIfAbsent",
          name: "Claim this release",
          config: { key: "deployed:{{trigger.payload.json.0.tag_name}}", value: "1" },
        },
        {
          actionType: "gate",
          name: "First time for this tag",
          // http.poll dedups on the whole response body, so an incidental change can re-fire for a
          // tag already deployed; gating on `acquired` makes the deploy idempotent per release tag.
          config: { value: "{{steps.1.output.acquired}}", isTruthy: true },
        },
        {
          actionType: "ssh.command",
          name: "Pull + restart",
          config: {
            host: "host.docker.internal",
            username: "youruser",
            privateKey: "{{connections.ssh.privateKey}}",
            // Detached on purpose: the SSH call returns immediately so this execution finishes
            // before `up -d` restarts the API (which is running this very workflow).
            command:
              "nohup sh -c 'sleep 5 && cd ~/automatex && " +
              "docker compose -f docker-compose.prod.yml pull && " +
              "docker compose -f docker-compose.prod.yml up -d' >> ~/automatex/deploy.log 2>&1 & echo scheduled",
          },
        },
        {
          actionType: "pushover.send",
          name: "Notify",
          config: {
            appToken: "{{connections.pushover.appToken}}",
            userKey: "{{connections.pushover.userKey}}",
            title: "AutomateX updating",
            message: "Deploying {{trigger.payload.json.0.tag_name}} — pulling and restarting.",
            priority: 0,
          },
        },
      ],
      triggers: [
        {
          type: "http.poll",
          config: {
            url: "https://api.github.com/repos/Eiromplays/AutomateX/releases",
            pollSeconds: 300,
            // For a private repo add: "Authorization": "Bearer {{connections.github.token}}"
            headers: { Accept: "application/vnd.github+json" },
          },
        },
      ],
    },
  },
];
