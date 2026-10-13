export function promptSnippet(prompt: string): string {
  const compact = prompt.replace(/\s+/g, ' ').trim();
  return compact.length > 180 ? compact.slice(0, 180).trimEnd() + '…' : compact;
}
