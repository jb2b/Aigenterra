import { spawn } from 'node:child_process';
if (!process.env.API_HTTP) {
  console.error(
    'API_HTTP is required. Start through Aigenterra.AppHost, or set it to the API HTTP endpoint.',
  );
  process.exit(1);
}
const port = process.env.PORT || '4200';
if (!/^\d+$/.test(port) || Number(port) < 1 || Number(port) > 65535) {
  console.error('PORT must be a valid TCP port.');
  process.exit(1);
}
const child = spawn(
  process.execPath,
  ['node_modules/@angular/cli/bin/ng.js', 'serve', '--host', '127.0.0.1', '--port', port],
  { stdio: 'inherit', env: process.env },
);
child.on('error', (error) => {
  console.error(error.message);
  process.exitCode = 1;
});
child.on('exit', (code) => {
  process.exitCode = code ?? 1;
});
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => child.kill(signal));
