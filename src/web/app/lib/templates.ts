// Curated starter workflows. "Use template" navigates to /workflows/new with this as
// the importDoc, so it flows through the same review screen as a file import — the user
// tweaks URLs / connection refs / rooms before creating. Connection refs and non-cron
// triggers (rss, etc.) are noted in the description for the user to wire up after.

type TemplateDoc = {
  automatex: number;
  name: string;
  description: string;
  steps: { actionType: string; name?: string; config: Record<string, unknown> }[];
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
];
