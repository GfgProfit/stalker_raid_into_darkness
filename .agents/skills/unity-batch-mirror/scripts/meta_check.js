const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { execSync } = require('child_process');
const project = process.env.STALKER_PROJECT || 'E:/Projects/stalker_raid_into_darkness';
const mirror = process.env.STALKER_MIRROR;
if (!mirror || !path.isAbsolute(mirror)) {
  console.error('Set STALKER_MIRROR to the authorized absolute mirror path.');
  process.exit(2);
}
const fix = process.argv.includes('--fix');
const create = process.argv.includes('--create');
const guid = file => {
  if (!fs.existsSync(file)) {
    return null;
  }
  const match = /^guid: ([0-9a-f]{32})/m.exec(fs.readFileSync(file, 'utf8'));
  return match ? match[1] : null;
};
const status = execSync('git -c core.quotepath=off status --porcelain --untracked-files=all Assets', { cwd: project, encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] });
let mismatches = 0;
let missing = 0;
for (const line of status.split('\n')) {
  let rel = line.slice(3).trim().replace(/^"|"$/g, '');
  if (!rel || line.startsWith(' D') || rel.endsWith('.meta')) {
    continue;
  }
  if (rel.endsWith('/')) {
    rel = rel.slice(0, -1);
  }
  let projectGuid = guid(path.join(project, rel + '.meta'));
  const mirrorGuid = guid(path.join(mirror, rel + '.meta'));
  if (projectGuid === null && create && rel.endsWith('.cs')) {
    const created = crypto.randomBytes(16).toString('hex');
    fs.writeFileSync(path.join(project, rel + '.meta'), `fileFormatVersion: 2\nguid: ${created}\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n`);
    console.log(`CREATED META ${rel} guid=${created}`);
  }
  if (guid(path.join(project, rel + '.meta')) === null) {
    missing++;
    console.log(`NO PROJECT META ${rel} (let the open editor import it first, or run with --create for .cs files)`);
    continue;
  }
  projectGuid = guid(path.join(project, rel + '.meta'));
  if (projectGuid === mirrorGuid) {
    continue;
  }
  mismatches++;
  console.log(`${mirrorGuid === null ? 'MISSING IN MIRROR' : 'GUID MISMATCH'} ${rel} project=${projectGuid} mirror=${mirrorGuid}`);
  if (fix && fs.existsSync(path.join(mirror, path.dirname(rel)))) {
    fs.copyFileSync(path.join(project, rel + '.meta'), path.join(mirror, rel + '.meta'));
    console.log('  fixed: project .meta copied into the mirror');
  }
}
console.log(`meta check: ${mismatches} mismatches, ${missing} without project meta${fix ? ' (fix applied)' : ''}`);
