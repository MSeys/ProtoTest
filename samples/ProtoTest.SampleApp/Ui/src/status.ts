/** One vocabulary for every state the API returns, so a badge never guesses. */

const TONES: Record<string, string> = {
  active: "success",
  archived: "neutral",
  succeeded: "success",
  failed: "danger",
  queued: "info",
  building: "info",
  rolled_back: "warning",
  paused: "warning",
  paid: "success",
  open: "warning",
  draft: "neutral",
  void: "neutral",
  uncollectible: "danger",
  trialing: "success",
  past_due: "warning",
  canceled: "danger",
  production: "accent",
  preview: "info"
};

export function statusTone(status: string): string {
  return TONES[status] ?? "neutral";
}

export function statusLabel(status: string): string {
  const text = status.replace(/_/g, " ");
  return text.charAt(0).toUpperCase() + text.slice(1);
}
