// Run after npm ci --prefix artifacts/mermaid-vendor-build --ignore-scripts.
import { build } from '../artifacts/mermaid-vendor-build/node_modules/esbuild/lib/main.js';
import { mkdir, readFile, writeFile, readdir } from 'node:fs/promises';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
const destination = 'src/Axiom.App/Assets/Mermaid';
await mkdir(destination, { recursive: true });
const result = await build({
  stdin: { contents: "import mermaid from 'mermaid'; window.axiomMermaid = mermaid;", resolveDir: 'artifacts/mermaid-vendor-build/node_modules' },
  bundle: true, format: 'iife', platform: 'browser', target: 'es2022', minify: true,
  legalComments: 'external', metafile: true, outfile: destination + '/mermaid.js'
});
if (Object.values(result.metafile.outputs).some(output => output.imports.length)) throw Error('Offline bundle contains external imports');
await writeFile(destination + '/package-lock.json', await readFile('artifacts/mermaid-vendor-build/package-lock.json'));
const bytes = await readFile(destination + '/mermaid.js');
await writeFile(destination + '/manifest.json', JSON.stringify({ version: '11.16.1', sha256: createHash('sha256').update(bytes).digest('hex'), source: 'https://github.com/mermaid-js/mermaid', offline: true }, null, 2));
const packages = new Set();
for (const input of Object.keys(result.metafile.inputs)) {
  const match = input.replaceAll('\\', '/').match(/^(.*node_modules\/(?:@[^/]+\/)?[^/]+)\//);
  if (match) packages.add(match[1]);
}
const notices = ['Offline Mermaid renderer: pinned transitive dependencies in package-lock.json.'];
for (const path of [...packages].sort()) {
  const metadata = JSON.parse(await readFile(join(path, 'package.json'), 'utf8'));
  notices.push(`\n=== ${metadata.name} ${metadata.version}; ${metadata.license} ===`);
  for (const name of await readdir(path)) {
    if (/^(license|licence|copying|notice)(\.|$)/i.test(name)) {
      try { notices.push(await readFile(join(path, name), 'utf8')); } catch {}
    }
  }
}
await writeFile(destination + '/THIRD-PARTY-NOTICES.txt', notices.join('\n'));
