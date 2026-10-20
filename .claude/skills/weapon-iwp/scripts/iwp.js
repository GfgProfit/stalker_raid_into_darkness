const fs = require('fs');
const path = require('path');

const ROOT = process.env.STALKER_IWP || 'E:/STALKER CoC IWP Unpacked pacth 16.03.2026';
const CP1251 = new TextDecoder('windows-1251');
const ADDON_CLASSES = new Set(['WP_SILEN', 'WP_SCOPE', 'WP_GLAUN', 'W_SILENC', 'W_SCOPE', 'W_GLAUNC']);

const read = file => CP1251.decode(fs.readFileSync(file));
const rel = file => path.relative(ROOT, file).replace(/\\/g, '/');

function loadLtx(entry) {
  const sections = new Map();
  const visited = new Set();
  const parseFile = file => {
    const key = path.resolve(file).toLowerCase();
    if (visited.has(key) || !fs.existsSync(file)) {
      return;
    }
    visited.add(key);
    let current = null;
    for (const raw of read(file).split(/\r?\n/)) {
      const line = raw.replace(/;.*$/, '').replace(/^\s*\/\/.*$/, '').trim();
      if (!line) {
        continue;
      }
      const include = /^#include\s+"([^"]+)"/.exec(line);
      if (include) {
        parseFile(path.join(path.dirname(file), include[1].replace(/\\/g, '/')));
        continue;
      }
      const header = /^\[([^\]]+)\]\s*(?::\s*(.*))?$/.exec(line);
      if (header) {
        const name = header[1].trim().toLowerCase();
        current = sections.get(name) || { name, parents: [], keys: new Map(), file: rel(file) };
        if (header[2]) {
          current.parents = header[2].split(',').map(item => item.trim().toLowerCase()).filter(Boolean);
        }
        sections.set(name, current);
        continue;
      }
      if (current) {
        const eq = line.indexOf('=');
        const k = (eq < 0 ? line : line.slice(0, eq)).trim();
        const v = eq < 0 ? '' : line.slice(eq + 1).trim().replace(/^"(.*)"$/, '$1');
        current.keys.set(k.toLowerCase(), v);
      }
    }
  };
  parseFile(entry);
  return sections;
}

let cache = null;
const db = () => cache || (cache = loadLtx(path.join(ROOT, 'configs/system.ltx')));

function resolve(name, seen = new Set()) {
  const section = db().get(name.toLowerCase());
  if (!section || seen.has(section.name)) {
    return new Map();
  }
  seen.add(section.name);
  const result = new Map();
  for (const parent of section.parents) {
    for (const [k, v] of resolve(parent, seen)) {
      result.set(k, v);
    }
  }
  for (const [k, v] of section.keys) {
    result.set(k, v);
  }
  return result;
}

function weaponSections() {
  const list = [];
  for (const section of db().values()) {
    const keys = resolve(section.name);
    const cls = keys.get('class') || '';
    if (/^WP_|^W_/.test(cls) && !section.file.startsWith('configs/mp/') && keys.has('inv_grid_width') && !ADDON_CLASSES.has(cls) && !section.name.endsWith('_hud')) {
      list.push({ section, keys });
    }
  }
  return list;
}

function walk(dir, out = []) {
  if (!fs.existsSync(dir)) {
    return out;
  }
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, out);
    } else {
      out.push(full);
    }
  }
  return out;
}

const WEAPONS_DIR = path.join(ROOT, 'meshes/dynamics/weapons');
const SERVICE_DIRS = new Set(['wpn_ammo', 'wpn_artefact', 'wpn_eat', 'wpn_grenades', 'wpn_hand', 'wpn_mine', 'wpn_mounted', 'wpn_shells', 'wpn_upgrade']);
const IWP_ALIASES = { fiveseven: ['fn57'], m700: ['rem700', 'remington700'], vector: ['krissvektor'], scarh: ['scar'], sr2m: ['sr2m1', 'sr2m2', 'sr2veresk'], saiga12: ['saiga'], sa58: ['falsa58osw'] };
const normalize = name => name.toLowerCase().replace(/^(wpn|w)_/, '').replace(/^eft_/, '').replace(/[^a-z0-9]/g, '');

function iwpKeys() {
  const keys = [];
  for (const dir of fs.readdirSync(WEAPONS_DIR)) {
    if (SERVICE_DIRS.has(dir) || !fs.statSync(path.join(WEAPONS_DIR, dir)).isDirectory()) {
      continue;
    }
    const key = normalize(dir);
    for (const alias of [key, ...(IWP_ALIASES[key] || [])]) {
      keys.push({ key: alias, dir: path.join(WEAPONS_DIR, dir) });
    }
  }
  return keys.sort((a, b) => b.key.length - a.key.length);
}

function graMatches() {
  const gra = require('../../gra-research/scripts/gra.js').ltx();
  const byName = new Map();
  for (const file of walk(WEAPONS_DIR)) {
    byName.set(path.basename(file).toLowerCase(), file);
  }
  const keys = iwpKeys();
  const rows = [];
  for (const [name, entry] of [...gra.sections].sort((a, b) => a[0].localeCompare(b[0]))) {
    const cls = gra.get(name, 'class') || '';
    const visual = (gra.get(name, 'visual') || '').replace(/\//g, '\\');
    if (!/^WP_/.test(cls) || ADDON_CLASSES.has(cls) || name.endsWith('_hud') || !visual || !gra.get(name, 'inv_grid_width')) {
      continue;
    }
    const parts = (visual.replace(/\.ogf$/i, '') + '.ogf').split('\\');
    const folder = parts[parts.length - 2] || '';
    const exact = path.join(ROOT, 'meshes', ...parts);
    const moved = byName.get(parts[parts.length - 1].toLowerCase());
    const graKey = normalize(folder);
    const same = keys.find(item => item.key === graKey);
    const prefix = same || keys.find(item => graKey.startsWith(item.key));
    let status = 'MISSING';
    let iwpModel = null;
    let iwpDir = null;
    if (fs.existsSync(exact)) {
      status = 'MODEL';
      iwpModel = exact;
    } else if (moved) {
      status = 'MOVED';
      iwpModel = moved;
    } else if (same) {
      status = 'FOLDER';
    } else if (prefix) {
      status = 'PREFIX';
    }
    iwpDir = iwpModel ? path.dirname(iwpModel) : (prefix ? prefix.dir : null);
    const hudFile = iwpModel ? path.join(iwpDir, path.basename(iwpModel, '.ogf') + '_hud.ogf') : null;
    rows.push({
      name, status, visual, folder, iwpModel, iwpDir, file: entry.file || '',
      hud: hudFile && fs.existsSync(hudFile) ? hudFile : null,
      omf: iwpDir ? fs.readdirSync(iwpDir).filter(file => file.endsWith('.omf')) : [],
      grid: `${gra.get(name, 'inv_grid_width')}x${gra.get(name, 'inv_grid_height')}`,
      silencer: `${gra.get(name, 'silencer_status') || 0} ${gra.get(name, 'silencer_name') || ''}`.trim()
    });
  }
  return rows;
}

const grid = keys => `${keys.get('inv_grid_width') || '?'}x${keys.get('inv_grid_height') || '?'}`;
const statusName = value => ({ 0: 'none', 1: 'permanent', 2: 'attachable' }[value] || value || 'none');

const commands = {
  weapons(filter) {
    const regex = filter ? new RegExp(filter, 'i') : null;
    for (const { section, keys } of weaponSections().sort((a, b) => a.section.name.localeCompare(b.section.name))) {
      if (regex && !regex.test(section.name)) {
        continue;
      }
      const sil = statusName(keys.get('silencer_status'));
      const silencer = sil === 'none' ? 'sil:none' : `sil:${sil}(${keys.get('silencer_name') || '?'} @${keys.get('silencer_x') || '?'},${keys.get('silencer_y') || '?'})`;
      console.log(`${section.name}\t${keys.get('class')}\tgrid ${grid(keys)}\tslot ${keys.get('slot') || '-'}\t${silencer}\tscope:${statusName(keys.get('scope_status'))}\tgl:${statusName(keys.get('grenade_launcher_status'))}\t${keys.get('visual') || ''}\thud=${keys.get('hud') || ''}\t[${section.file}]`);
    }
  },
  addons() {
    for (const section of db().values()) {
      const keys = resolve(section.name);
      if (ADDON_CLASSES.has(keys.get('class'))) {
        console.log(`${section.name}\t${keys.get('class')}\tgrid ${grid(keys)}\tweight ${keys.get('inv_weight') || '-'}\t${keys.get('visual') || ''}\t[${section.file}]`);
      }
    }
  },
  compat(addon) {
    const name = addon.toLowerCase();
    for (const { section, keys } of weaponSections()) {
      const scopes = (keys.get('scopes_sect') || '').split(',').map(item => item.trim().toLowerCase());
      const hits = [];
      for (const kind of ['silencer', 'scope', 'grenade_launcher']) {
        if ((keys.get(`${kind}_name`) || '').toLowerCase() === name) {
          hits.push(`${kind} ${statusName(keys.get(`${kind}_status`))} @${keys.get(`${kind}_x`)},${keys.get(`${kind}_y`)}`);
        }
      }
      for (const scope of scopes) {
        const scopeKeys = resolve(scope);
        if ((scopeKeys.get('scope_name') || '').toLowerCase() === name) {
          hits.push(`scope via [${scope}] @${scopeKeys.get('scope_x')},${scopeKeys.get('scope_y')}`);
        }
      }
      if (hits.length) {
        console.log(`${section.name}\tgrid ${grid(keys)}\t${hits.join('; ')}`);
      }
    }
  },
  sections(filter) {
    const regex = new RegExp(filter, 'i');
    for (const section of db().values()) {
      if (regex.test(section.name)) {
        console.log(`${section.name}${section.parents.length ? ':' + section.parents.join(',') : ''}\t[${section.file}]`);
      }
    }
  },
  dump(...names) {
    for (const name of names) {
      const section = db().get(name.toLowerCase());
      if (!section) {
        console.log(`[${name}] not found`);
        continue;
      }
      console.log(`[${section.name}]${section.parents.length ? ':' + section.parents.join(',') : ''}  (${section.file})`);
      for (const [k, v] of [...resolve(name)].sort((a, b) => a[0].localeCompare(b[0]))) {
        console.log(`${section.keys.has(k) ? ' ' : '^'} ${k} = ${v}`);
      }
    }
  },
  get(name, key) {
    console.log(resolve(name).get(key.toLowerCase()) ?? '(missing)');
  },
  meshes(filter) {
    const regex = new RegExp(filter || '.', 'i');
    for (const file of walk(path.join(ROOT, 'meshes/dynamics/weapons'))) {
      if (regex.test(rel(file))) {
        console.log(`${rel(file)}\t${fs.statSync(file).size}`);
      }
    }
  },
  orphans() {
    const used = new Set();
    for (const section of db().values()) {
      for (const [k, v] of section.keys) {
        if (/visual/.test(k) && v) {
          used.add(v.toLowerCase().replace(/\//g, '\\').replace(/\.ogf$/, ''));
        }
      }
    }
    for (const file of walk(path.join(ROOT, 'meshes/dynamics/weapons'))) {
      if (!file.endsWith('.ogf') || /_lod\.ogf$/.test(file)) {
        continue;
      }
      const virtual = rel(file).replace(/^meshes\//, '').replace(/\.ogf$/, '').replace(/\//g, '\\').toLowerCase();
      if (!used.has(virtual)) {
        console.log(rel(file));
      }
    }
  },
  grep(pattern, subdir = 'configs', ext = '') {
    const regex = new RegExp(pattern, 'i');
    for (const file of walk(path.join(ROOT, subdir))) {
      if (ext && !file.toLowerCase().endsWith(ext.toLowerCase())) {
        continue;
      }
      if (!/\.(ltx|script|xml|txt)$/i.test(file)) {
        continue;
      }
      read(file).split(/\r?\n/).forEach((line, index) => {
        if (regex.test(line)) {
          console.log(`${rel(file)}:${index + 1}: ${line.trim()}`);
        }
      });
    }
  },
  strings(id) {
    const prefix = id.endsWith('*') ? id.slice(0, -1) : null;
    for (const file of walk(path.join(ROOT, 'configs/text/rus'))) {
      const text = read(file);
      const regex = /<string\s+id="([^"]+)"[^>]*>\s*<text>([\s\S]*?)<\/text>/g;
      let match;
      while ((match = regex.exec(text))) {
        if (prefix !== null ? match[1].startsWith(prefix) : match[1] === id) {
          console.log(`${match[1]} = ${match[2].trim()}  [${rel(file)}]`);
        }
      }
    }
  },
  sounds(name) {
    const keys = resolve(name);
    for (const [k, v] of keys) {
      if (k.startsWith('snd_') && v) {
        const file = v.split(',')[0].trim().replace(/\\/g, '/');
        const exists = fs.existsSync(path.join(ROOT, 'sounds', file + '.ogg'));
        console.log(`${k} = ${v}${exists ? '' : '   (MISSING sounds/' + file + '.ogg)'}`);
      }
    }
  },
  match(filter) {
    const regex = filter ? new RegExp(filter, 'i') : null;
    const counts = {};
    for (const row of graMatches()) {
      if (regex && !regex.test(row.name)) {
        continue;
      }
      counts[row.status] = (counts[row.status] || 0) + 1;
      const target = row.iwpDir ? ' -> ' + rel(row.iwpModel || row.iwpDir) : '';
      console.log(`${row.name}\t${row.status}\t${row.visual}${target}\thud:${row.hud ? rel(row.hud) : '-'}\tomf:${row.omf.join(',') || '-'}\tgra grid ${row.grid}\tsil ${row.silencer}\t[${row.file}]`);
    }
    console.log('match: ' + Object.entries(counts).map(([k, v]) => `${v} ${k}`).join(', '));
  },
  iwpmap(filter) {
    const regex = filter ? new RegExp(filter, 'i') : null;
    const byDir = new Map();
    for (const row of graMatches()) {
      if (row.iwpDir) {
        const key = path.basename(row.iwpDir);
        byDir.set(key, (byDir.get(key) || []).concat(row));
      }
    }
    for (const dir of fs.readdirSync(WEAPONS_DIR).sort()) {
      if (SERVICE_DIRS.has(dir) || (regex && !regex.test(dir))) {
        continue;
      }
      const rows = byDir.get(dir) || [];
      const exact = rows.filter(row => row.status !== 'PREFIX');
      const bases = [...new Set((exact.length ? exact : rows).map(row => row.folder))];
      console.log(`${dir}\t${rows.length ? rows.length + ' GRA sections' : 'NO GRA SECTION'}\t${exact.length ? '' : rows.length ? '(prefix only) ' : ''}${bases.join(', ')}`);
    }
  },
  motions(...names) {
    const found = {};
    for (const file of walk(path.join(ROOT, 'meshes')).filter(item => item.endsWith('.omf'))) {
      const data = fs.readFileSync(file);
      for (const name of names) {
        if (data.indexOf(Buffer.from(name + '\0', 'latin1')) >= 0) {
          (found[name] = found[name] || []).push(rel(file));
        }
      }
    }
    names.forEach(name => console.log(`${name} => ${(found[name] || ['NOT FOUND']).join(', ')}`));
  },
  omfnames(filter) {
    const regex = new RegExp(filter, 'i');
    for (const file of walk(path.join(ROOT, 'meshes')).filter(item => item.endsWith('.omf') && regex.test(rel(item)))) {
      const names = new Set(fs.readFileSync(file).toString('latin1').match(/[a-z0-9_]{4,}(?=\0)/gi) || []);
      console.log(`${rel(file)}: ${[...names].filter(name => !/^bip01|^wpn_body$|^bone/i.test(name)).join(' ')}`);
    }
  }
};

module.exports = { ROOT, loadLtx, resolve, weaponSections };

if (require.main === module) {
  const [command, ...args] = process.argv.slice(2);
  if (!commands[command]) {
    console.log('usage: node iwp.js <weapons [regex] | addons | compat <addon> | sections <regex> | dump <sec>... | get <sec> <key> | meshes [regex] | orphans | grep <regex> [subdir] [.ext] | strings <id|prefix*> | sounds <sec> | match [regex] | iwpmap [regex] | motions <name>... | omfnames <path-regex>>');
    process.exit(2);
  }
  commands[command](...args);
}
