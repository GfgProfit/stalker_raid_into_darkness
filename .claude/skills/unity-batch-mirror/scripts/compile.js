const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const project = process.env.STALKER_PROJECT || 'E:/Projects/stalker_raid_into_darkness';
const out = process.env.STALKER_BUILD || path.join(process.env.STALKER_WORK || path.join(path.dirname(process.env.STALKER_MIRROR || 'C:/Users/kagami/AppData/Local/Temp/claude/e--Projects-stalker-raid-into-darkness/f9f30669-85fe-4c0e-9402-a46207e682fe/scratchpad/CleanProject'), 'batch'), 'build');
const names = process.argv.slice(2);
if (names.length === 0) {
  console.log('usage: compile.js <AssemblyName>...   (for example RaidIntoDarkness.Player.Editor)');
  process.exit(2);
}
fs.mkdirSync(out, { recursive: true });
const asmdefs = new Map();
const guidToName = new Map();
const scan = dir => {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      scan(full);
    } else if (entry.name.endsWith('.asmdef')) {
      const data = JSON.parse(fs.readFileSync(full, 'utf8').replace(/^\uFEFF/, ''));
      asmdefs.set(data.name, { dir: path.dirname(full), data });
      const meta = full + '.meta';
      if (fs.existsSync(meta)) {
        const guid = (/^guid: ([0-9a-f]{32})/m.exec(fs.readFileSync(meta, 'utf8')) || [])[1];
        if (guid) {
          guidToName.set(guid, data.name);
        }
      }
    }
  }
};
scan(path.join(project, 'Assets'));
const collectSources = (dir, root, result) => {
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name.endsWith('~') || entry.name.startsWith('.')) {
        continue;
      }
      const nested = fs.readdirSync(full).some(name => name.endsWith('.asmdef'));
      if (!nested) {
        collectSources(full, root, result);
      }
    } else if (entry.name.endsWith('.cs')) {
      result.push(full);
    }
  }
  return result;
};
const done = new Set();
const convert = name => {
  if (done.has(name)) {
    return;
  }
  done.add(name);
  const source = path.join(project, name + '.csproj');
  if (!fs.existsSync(source)) {
    throw new Error(`${name}.csproj is missing in the project root; let the open editor regenerate project files`);
  }
  let text = fs.readFileSync(source, 'utf8');
  text = text.replace(/<(None|ProjectReference) Include="([^"]+)"/g, (match, kind, value) => {
    if (kind === 'ProjectReference') {
      convert(path.basename(value, '.csproj'));
      return match;
    }
    return `<${kind} Include="${path.join(project, value)}"`;
  });
  text = text.replace(/<HintPath>(?![A-Za-z]:)([^<]+)<\/HintPath>/g, (match, value) => `<HintPath>${path.join(project, value)}</HintPath>`);
  text = text.replace(/Temp\\obj/g, 'obj').replace(/Temp\\bin/g, 'bin');
  const asmdef = asmdefs.get(name);
  if (asmdef) {
    text = text.replace(/\s*<Compile Include="[^"]+" \/>/g, '');
    const sources = collectSources(asmdef.dir, asmdef.dir, []);
    const additions = [];
    additions.push(...sources.map(file => `<Compile Include="${file}" />`));
    for (let reference of asmdef.data.references || []) {
      if (reference.startsWith('GUID:')) {
        reference = guidToName.get(reference.slice(5)) || reference;
      }
      if (fs.existsSync(path.join(project, reference + '.csproj'))) {
        if (!text.includes(`Include="${reference}.csproj"`)) {
          additions.push(`<ProjectReference Include="${reference}.csproj" />`);
        }
        convert(reference);
        continue;
      }
      const dll = path.join(project, 'Library', 'ScriptAssemblies', reference + '.dll');
      if (fs.existsSync(dll) && !text.includes(`${reference}.dll`)) {
        additions.push(`<Reference Include="${reference}"><HintPath>${dll}</HintPath><Private>False</Private></Reference>`);
      }
    }
    text = text.replace('</Project>', `<ItemGroup>${additions.join('')}</ItemGroup></Project>`);
  } else {
    text = text.replace(/<Compile Include="([^"]+)"/g, (match, value) => `<Compile Include="${path.join(project, value)}"`);
  }
  fs.writeFileSync(path.join(out, name + '.csproj'), text);
};
for (const name of names) {
  convert(name);
}
let failed = false;
for (const name of names) {
  try {
    const output = execFileSync('dotnet', ['build', path.join(out, name + '.csproj'), '--no-incremental', '-nologo', '-clp:ErrorsOnly', '-v', 'q'], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
    const errors = [...new Set(output.split('\n').filter(line => /error CS/.test(line)).map(line => line.replace(/\s*\[[^\]]+\]\s*$/, '').trim()))];
    console.log(`${name}: ${errors.length === 0 ? 'OK' : errors.length + ' errors'}`);
    errors.slice(0, 30).forEach(line => console.log('  ' + line));
    failed = failed || errors.length > 0;
  } catch (error) {
    failed = true;
    const output = String(error.stdout || '') + String(error.stderr || '');
    const errors = [...new Set(output.split('\n').filter(line => /error/.test(line)).map(line => line.replace(/\s*\[[^\]]+\]\s*$/, '').trim()))];
    console.log(`${name}: FAILED`);
    errors.slice(0, 30).forEach(line => console.log('  ' + line));
  }
}
process.exit(failed ? 1 : 0);
