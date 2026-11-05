import fs from 'node:fs';
import path from 'node:path';
export const pluginId = 'opencode.manager.autonomy.v1';
export function canonical(value) {
  let current = path.resolve(value), rest = [];
  while (!fs.existsSync(current)) {
    const parent = path.dirname(current);
    if (parent === current) throw new Error('No existing ancestor');
    rest.unshift(path.basename(current)); current = parent;
  }
  return path.join(fs.realpathSync(current), ...rest);
}
export const within = (target, root) => target === root || target.startsWith(root + path.sep);
export function secret(value) {
  return value.split(/[\\/]/).some(p => /^(\.env(?:\.|$)|\.ssh$|\.gnupg$|\.aws$|\.codex$|\.opencode$|\.agents$|\.claude$|opencode\.jsonc?$|\.git$|secrets?$|credentials?|auth\.json$|service\.json$|id_(rsa|ed25519)|.*\.(key|pem|pfx)$)/i.test(p));
}
export function projectPath(resource, root, cwd = root) {
  if (typeof resource !== 'string' || /[\0\n\r]/.test(resource) || secret(resource)) return false;
  const prefix = resource.split(/[*?\[]/, 1)[0] || '.';
  try { return within(canonical(path.resolve(cwd, prefix)), root); } catch { return false; }
}
export function commandRisk(command) {
  return /\b(?:sudo|su|doas|pkexec|mkfs\w*|wipefs|shutdown|reboot|mount|umount)\b|\bgit\b[^\n;&|]*(?:\bpush\b|\breset\s+--hard\b|\bclean\b)|\b(?:drop|truncate)\s+(?:database|schema|table)\b|\b(?:rails|rake)\s+db:(?:drop|reset|purge)|\bdotnet\s+ef\s+database\b/i.test(command);
}
export function decide(action, resources, root, cwd = root) {
  if (!Array.isArray(resources) || resources.length === 0) return 'deny';
  if (['read','edit','write','glob','grep','list','ls'].includes(action)) return resources.every(r => projectPath(r, root, cwd) && (!['edit','write'].includes(action) || !/\.(?:db|sqlite|sqlite3)(?:-(?:wal|shm))?$/i.test(r))) ? 'allow' : 'deny';
  if (['skill','subagent','agent'].includes(action)) return resources.every(r => typeof r === 'string' && /^[\w.-]+$/.test(r)) ? 'allow' : 'deny';
  return 'deny';
}
