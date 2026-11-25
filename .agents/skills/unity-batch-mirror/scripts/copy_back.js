const fs = require('fs');
const path = require('path');
const project = process.env.STALKER_PROJECT || 'E:/Projects/stalker_raid_into_darkness';
const mirror = process.env.STALKER_MIRROR;
if (!mirror || !path.isAbsolute(mirror)) {
  console.error('Set STALKER_MIRROR to the authorized absolute mirror path.');
  process.exit(2);
}
const work = process.env.STALKER_WORK || path.join(path.dirname(mirror), 'batch');
const args = process.argv.slice(2);
const apply = args.includes('--apply');
const force = args.includes('--force');
const targets = args.filter(arg => !arg.startsWith('--'));
const CODE = /\.(cs|asmdef|asmref|shader|hlsl|cginc|inputactions)$/i;
if (targets.length === 0) {
  console.log('usage: copy_back.js <Assets/relative/path>... [--apply] [--force]');
  process.exit(2);
}
const lastSyncFile = path.join(work, 'last_sync');
const lastSync = fs.existsSync(lastSyncFile) ? Number(fs.readFileSync(lastSyncFile, 'utf8').trim()) * 1000 : 0;
const stamp = new Date().toISOString().replace(/[:.]/g, '-');
const backupRoot = path.join(work, 'backup', stamp);
const files = [];
const collect = rel => {
  const full = path.join(mirror, rel);
  if (!fs.existsSync(full)) {
    console.log(`MISSING IN MIRROR ${rel}`);
    return;
  }
  if (fs.statSync(full).isDirectory()) {
    for (const name of fs.readdirSync(full)) {
      collect(path.join(rel, name));
    }
    return;
  }
  files.push(rel);
};
for (const target of targets) {
  collect(target);
  if (fs.existsSync(path.join(mirror, target + '.meta'))) {
    files.push(target + '.meta');
  }
}
const counts = { created: 0, updated: 0, same: 0, skippedCode: 0, conflicts: 0 };
for (const rel of files) {
  if (CODE.test(rel)) {
    counts.skippedCode++;
    continue;
  }
  const source = path.join(mirror, rel);
  const destination = path.join(project, rel);
  const exists = fs.existsSync(destination);
  if (exists && fs.readFileSync(source).equals(fs.readFileSync(destination))) {
    counts.same++;
    continue;
  }
  if (exists && lastSync > 0 && fs.statSync(destination).mtimeMs > lastSync && !force) {
    counts.conflicts++;
    console.log(`CONFLICT ${rel}: changed in the project after the last sync (user edit?)`);
    continue;
  }
  if (!apply) {
    console.log(`${exists ? 'UPDATE' : 'CREATE'} ${rel}`);
    counts[exists ? 'updated' : 'created']++;
    continue;
  }
  if (exists) {
    fs.mkdirSync(path.dirname(path.join(backupRoot, rel)), { recursive: true });
    fs.copyFileSync(destination, path.join(backupRoot, rel));
  }
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(source, destination);
  counts[exists ? 'updated' : 'created']++;
}
console.log(`${apply ? 'copied' : 'dry run'}: ${JSON.stringify(counts)}${apply && counts.updated > 0 ? ' backup ' + backupRoot : ''}`);
