import path from 'node:path';
import { canonical, within, decide, commandRisk } from './policy.mjs';
const quote = value => "'" + value.replaceAll("'", "'\\''") + "'";
export async function install(ctx, config) {
  async function scope(sessionID) {
    const seen = new Set(); let session, workingDirectory;
    for (let i = 0; sessionID && i < 24; i++) {
      if (seen.has(sessionID)) throw new Error('Session parent cycle'); seen.add(sessionID);
      const result = await ctx.session.get({ sessionID }); session = result.data ?? result;
      workingDirectory ??= session.location?.directory;
      const marker = session.metadata?.opencodeManagerAutonomy;
      if (marker?.version === 1 && marker.enabled === true) {
        const root = canonical(marker.directory);
        if (!config.roots.some(r => within(root, canonical(r)))) throw new Error('Untrusted manager root');
        if (!workingDirectory || canonical(session.location.directory) !== root || !within(canonical(workingDirectory),root)) throw new Error('Managed session moved outside its sandbox');
        return { root, cwd: canonical(workingDirectory) };
      }
      sessionID = session.parentID;
    }
  }
  await ctx.permission.hook('evaluate', async event => {
    const managed = await scope(event.sessionID);
    const root = managed?.root;
    if (!root) return;
    // Explicit denies are never overridden by this profile.
    if (event.effect === 'deny') return;
    const escaped = [...config.runner].map(c => '\\^$.*+?()[]{}|'.includes(c) ? '\\' + c : c).join('');
    const exact = new RegExp(`^/usr/bin/python3 (?:'${escaped}'|${escaped}) (?:'[A-Za-z0-9_-]+'|[A-Za-z0-9_-]+)$`);
    event.effect = event.action === 'shell' && event.resources.length > 0 && event.resources.every(r => exact.test(r)) ? 'allow' : decide(event.action, event.resources, root, managed.cwd);
    if (event.effect === 'deny') event.message = 'Autonomous project policy denied this action. Use project-local files and the isolated shell; do not request host privileges or secrets.';
  });
  await ctx.tool.transform(editor => {
    editor.update('shell', tool => {
      const original = tool.execute;
      tool.execute = async (input, context) => {
        const managed = await scope(context.sessionID);
        const root = managed?.root;
        if (!root) return original(input, context);
        if (typeof input.command !== 'string' || commandRisk(input.command)) throw new Error('Autonomous shell denied a privileged/destructive operation');
        const cwd = canonical(path.resolve(managed.cwd, input.workdir ?? '.'));
        if (!within(cwd, root)) throw new Error('Shell workdir is outside the selected project');
        const payload = Buffer.from(JSON.stringify({ root, cwd, command: input.command })).toString('base64url');
        // Wrapper permissions are handled for this invocation only. The original command is never approved on the host.
        const wrapped = `/usr/bin/python3 ${quote(config.runner)} ${quote(payload)}`;
        return original({ ...input, command: wrapped, workdir: cwd, background: false, timeout: Math.min(Math.max(input.timeout ?? 120000, 1000), 600000) }, {
          ...context,
        });
      };
    });
  });
}
