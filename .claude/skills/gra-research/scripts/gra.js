const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const GRA = process.env.GRA_ROOT || 'D:/GRA2.5';
const BASE = process.env.GRA_BASE || path.join(GRA, 'Anomaly 1.5.3 up/tools/_unpacked');
const MO2 = process.env.GRA_MO2 || path.join(GRA, 'MO2');
const APPDATA = path.join(GRA, 'Anomaly 1.5.3 up/appdata');
const cp1251 = new TextDecoder('windows-1251');

function selectedProfile() {
  if (process.env.GRA_PROFILE) {
    return process.env.GRA_PROFILE;
  }
  const ini = fs.readFileSync(path.join(MO2, 'ModOrganizer.ini'), 'latin1');
  let raw = /selected_profile=(.*)/.exec(ini)[1].trim().replace(/^"|"$/g, '');
  if (raw.startsWith('@ByteArray(') && raw.endsWith(')')) {
    raw = raw.slice('@ByteArray('.length, -1);
  }
  const bytes = [];
  for (let i = 0; i < raw.length; i++) {
    if (raw[i] === '\\' && raw[i + 1] === 'x') {
      bytes.push(parseInt(raw.substr(i + 2, 2), 16));
      i += 3;
    } else {
      bytes.push(raw.charCodeAt(i));
    }
  }
  return Buffer.from(bytes).toString('utf8');
}

const PROFILE = selectedProfile();
const MODS = fs.readFileSync(path.join(MO2, 'profiles', PROFILE, 'modlist.txt'), 'utf8').split(/\r?\n/).filter(line => line.startsWith('+')).map(line => line.slice(1));

function roots() {
  const result = [{ label: 'overwrite', root: path.join(MO2, 'overwrite') }];
  for (const mod of MODS) {
    result.push({ label: mod, root: path.join(MO2, 'mods', mod, 'gamedata') });
  }
  result.push({ label: 'base', root: BASE });
  return result;
}

function candidates(rel) {
  rel = rel.replace(/\\/g, '/').replace(/^gamedata\//, '');
  const result = [];
  for (const { label, root } of roots()) {
    const full = path.join(root, rel);
    if (fs.existsSync(full) && fs.statSync(full).isFile()) {
      result.push({ label, path: full });
    }
  }
  return result;
}

function winner(rel) {
  return candidates(rel)[0] || null;
}

function winners(subdir, extension) {
  const map = new Map();
  for (const { label, root } of roots()) {
    const start = path.join(root, subdir);
    if (!fs.existsSync(start)) {
      continue;
    }
    const walk = dir => {
      for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
        const full = path.join(dir, entry.name);
        if (entry.isDirectory()) {
          walk(full);
          continue;
        }
        if (extension && !entry.name.toLowerCase().endsWith(extension)) {
          continue;
        }
        const rel = path.relative(root, full).split(path.sep).join('/').toLowerCase();
        if (!map.has(rel)) {
          map.set(rel, { label, path: full });
        }
      }
    };
    walk(start);
  }
  return map;
}

let ltxCache = null;
function ltx() {
  if (ltxCache) {
    return ltxCache;
  }
  const files = winners('configs', '.ltx');
  const sections = new Map();
  const seen = new Set();
  const include = (rel) => {
    rel = rel.toLowerCase();
    const dir = path.posix.dirname(rel);
    const base = path.posix.basename(rel);
    if (base.includes('*')) {
      const pattern = new RegExp('^' + base.replace(/\./g, '[.]').replace(/\*/g, '.*') + '$');
      for (const key of [...files.keys()].sort()) {
        if (path.posix.dirname(key) === dir && pattern.test(path.posix.basename(key))) {
          parseRel(key);
        }
      }
    } else {
      parseRel(rel);
    }
  };
  const parseRel = rel => {
    if (seen.has(rel) || !files.has(rel)) {
      return;
    }
    seen.add(rel);
    parse(rel);
  };
  const parse = rel => {
    const text = cp1251.decode(fs.readFileSync(files.get(rel).path));
    let current = null;
    for (let line of text.split(/\r?\n/)) {
      const inc = /^\s*#include\s+"([^"]+)"/.exec(line);
      if (inc) {
        const target = inc[1].replace(/\\/g, '/');
        const dir = path.posix.dirname(rel);
        include(dir === '.' ? target : dir + '/' + target);
        continue;
      }
      line = line.replace(/;.*$/, '').replace(/\/\/.*$/, '').trim();
      if (!line) {
        continue;
      }
      const header = /^(!!|!|@)?\[([^\]]+)\]\s*(?::\s*(.*))?$/.exec(line);
      if (header) {
        const name = header[2].trim().toLowerCase();
        const parents = header[3] ? header[3].split(',').map(s => s.trim().toLowerCase()).filter(Boolean) : null;
        if (header[1] === '!' || header[1] === '@') {
          current = sections.get(name);
          if (!current) {
            if (header[1] === '!') {
              current = null;
              continue;
            }
            current = { parents: [], keys: {}, file: rel };
            sections.set(name, current);
          }
          if (parents) {
            current.parents = parents;
          }
        } else {
          current = { parents: parents || [], keys: {}, file: rel };
          sections.set(name, current);
        }
        continue;
      }
      if (!current) {
        continue;
      }
      const pair = /^(!|>|<)?([^=]+?)\s*(?:=\s*(.*))?$/.exec(line);
      if (!pair) {
        continue;
      }
      const key = pair[2].trim().toLowerCase();
      const value = (pair[3] || '').trim();
      if (pair[1] === '!') {
        delete current.keys[key];
      } else if (pair[1] === '>') {
        current.keys[key] = current.keys[key] ? current.keys[key] + ',' + value : value;
      } else if (pair[1] === '<') {
        const removed = value.split(',').map(s => s.trim());
        current.keys[key] = (current.keys[key] || '').split(',').map(s => s.trim()).filter(s => s && !removed.includes(s)).join(',');
      } else {
        current.keys[key] = value;
      }
    }
  };
  parseRel('configs/system.ltx');
  for (const rel of [...files.keys()].sort((a, b) => path.posix.basename(a).localeCompare(path.posix.basename(b)))) {
    if (path.posix.dirname(rel) === 'configs' && path.posix.basename(rel).startsWith('mod_system_')) {
      parse(rel);
    }
  }
  const get = (section, key, depth = 0) => {
    const entry = sections.get(section.toLowerCase());
    if (!entry || depth > 20) {
      return undefined;
    }
    if (key in entry.keys) {
      return entry.keys[key];
    }
    for (let i = entry.parents.length - 1; i >= 0; i--) {
      const value = get(entry.parents[i], key, depth + 1);
      if (value !== undefined) {
        return value;
      }
    }
    return undefined;
  };
  const dump = section => {
    const all = {};
    const collect = (name, depth) => {
      const entry = sections.get(name);
      if (!entry || depth > 20) {
        return;
      }
      for (const parent of entry.parents) {
        collect(parent, depth + 1);
      }
      Object.assign(all, entry.keys);
    };
    collect(section.toLowerCase(), 0);
    return all;
  };
  ltxCache = { sections, get: (s, k) => get(s, k.toLowerCase()), dump, files };
  return ltxCache;
}

function strings(ids) {
  const files = winners('configs/text/rus', '.xml');
  const result = {};
  for (const [, file] of files) {
    const text = cp1251.decode(fs.readFileSync(file.path));
    const pattern = /<string\s+id="([^"]+)"\s*>\s*<text>([\s\S]*?)<\/text>/g;
    let match;
    while ((match = pattern.exec(text))) {
      if (ids.some(id => match[1] === id || (id.endsWith('*') && match[1].startsWith(id.slice(0, -1))))) {
        result[match[1]] = match[2];
      }
    }
  }
  return result;
}

function motions(names) {
  const folders = ['meshes/anomaly_weapons/hud_hands_animation', 'meshes/anomaly_weapons/hud_animation', 'meshes/sm/actor_legs', 'meshes/actors'];
  const found = {};
  for (const folder of folders) {
    for (const [rel, file] of winners(folder, '.omf')) {
      const data = fs.readFileSync(file.path);
      for (const name of names) {
        if (!found[name] && data.indexOf(Buffer.from(name + '\0', 'latin1')) >= 0) {
          found[name] = `${rel} [${file.label}]`;
        }
      }
    }
  }
  return names.map(name => `${name} => ${found[name] || 'NOT FOUND'}`);
}

function ogfTextures(file) {
  const buffer = fs.readFileSync(file);
  const out = [];
  const walk = (start, end) => {
    let offset = start;
    while (offset + 8 <= end) {
      const id = buffer.readUInt32LE(offset) & 0x7fffffff;
      const size = buffer.readUInt32LE(offset + 4);
      const data = offset + 8;
      if (data + size > end) {
        break;
      }
      if (id === 2) {
        let p = data;
        const z = () => {
          let q = p;
          while (buffer[q]) {
            q++;
          }
          const s = buffer.toString('latin1', p, q);
          p = q + 1;
          return s;
        };
        out.push([z(), z()]);
      } else if (id === 9) {
        let q = data;
        while (q + 8 <= data + size) {
          const childSize = buffer.readUInt32LE(q + 4);
          walk(q + 8, q + 8 + childSize);
          q += 8 + childSize;
        }
      }
      offset = data + size;
    }
  };
  walk(0, buffer.length);
  return out;
}

function settings(pattern) {
  const regex = new RegExp(pattern, 'i');
  const sources = [path.join(APPDATA, 'user.ltx'), path.join(MO2, 'overwrite/gamedata/configs/axr_options.ltx')];
  const lines = [];
  for (const file of sources) {
    if (!fs.existsSync(file)) {
      continue;
    }
    for (const line of fs.readFileSync(file, 'utf8').split(/\r?\n/)) {
      if (regex.test(line)) {
        lines.push(`${path.basename(file)}: ${line.trim()}`);
      }
    }
  }
  return lines;
}

module.exports = { PROFILE, MODS, BASE, MO2, candidates, winner, winners, ltx, strings, motions, ogfTextures };

if (require.main === module) {
  const [command, ...args] = process.argv.slice(2);
  const md5 = file => crypto.createHash('md5').update(fs.readFileSync(file)).digest('hex').slice(0, 8);
  switch (command) {
    case 'profile':
      console.log(`${PROFILE}: ${MODS.length} enabled mods (first = highest priority)`);
      break;
    case 'winner':
      for (const rel of args) {
        console.log('== ' + rel);
        candidates(rel).forEach((c, i) => console.log(`   ${i === 0 ? 'WIN ' : '    '}${c.label}  ${md5(c.path)}  ${fs.statSync(c.path).size}  ${c.path}`));
      }
      break;
    case 'find': {
      const regex = new RegExp(args[0], 'i');
      for (const [rel, file] of winners(args[1] || '', args[2])) {
        if (regex.test(rel)) {
          console.log(`${rel} [${file.label}]`);
        }
      }
      break;
    }
    case 'grep': {
      const regex = new RegExp(args[0], 'i');
      for (const [rel, file] of winners(args[1], args[2])) {
        const text = cp1251.decode(fs.readFileSync(file.path));
        text.split(/\r?\n/).forEach((line, index) => {
          if (regex.test(line)) {
            console.log(`${rel}:${index + 1} [${file.label}] ${line.trim().slice(0, 240)}`);
          }
        });
      }
      break;
    }
    case 'get':
      console.log(ltx().get(args[0], args[1]));
      break;
    case 'dump':
      for (const section of args) {
        const entry = ltx().sections.get(section.toLowerCase());
        console.log(`[${section}] ${entry ? 'parents=' + entry.parents.join(',') + ' file=' + entry.file : 'MISSING'}`);
        if (entry) {
          for (const [key, value] of Object.entries(ltx().dump(section))) {
            console.log(`  ${key} = ${value}`);
          }
        }
      }
      break;
    case 'sections': {
      const regex = new RegExp(args[0], 'i');
      for (const [name, entry] of ltx().sections) {
        if (regex.test(name)) {
          console.log(`${name}  (${entry.file})`);
        }
      }
      break;
    }
    case 'strings':
      console.log(JSON.stringify(strings(args), null, 1));
      break;
    case 'motions':
      motions(args).forEach(line => console.log(line));
      break;
    case 'ogftex':
      for (const arg of args) {
        const file = fs.existsSync(arg) ? arg : (winner(arg) || {}).path;
        console.log(arg, file ? JSON.stringify(ogfTextures(file)) : 'NOT FOUND');
      }
      break;
    case 'settings':
      settings(args[0] || '.').forEach(line => console.log(line));
      break;
    default:
      console.log('usage: gra.js profile | winner <rel>... | find <regex> [subdir] [.ext] | grep <regex> <subdir> [.ext] | get <section> <key> | dump <section>... | sections <regex> | strings <id|prefix*>... | motions <name>... | ogftex <rel|path>... | settings <regex>');
  }
}
