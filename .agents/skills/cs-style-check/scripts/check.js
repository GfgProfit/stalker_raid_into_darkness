const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const project = process.env.STALKER_PROJECT || 'E:/Projects/stalker_raid_into_darkness';
const args = process.argv.slice(2);
const all = args.includes('--all');
const staged = args.includes('--staged');
const explicit = args.filter(arg => !arg.startsWith('--'));
const CODE_EXTENSIONS = /\.(cs|shader|hlsl|js|sh|ps1)$/i;
const THIRD_PARTY = /^Assets\/(Plugins|TextMesh Pro)\//;

const git = command => execSync(`git -c core.quotepath=off ${command}`, { cwd: project, encoding: 'utf8', maxBuffer: 1 << 28, stdio: ['ignore', 'pipe', 'ignore'] });

function addedLines() {
  const result = new Map();
  if (explicit.length > 0 || all) {
    const files = explicit.length > 0 ? explicit : git('ls-files').split('\n').filter(file => file.endsWith('.cs') && !THIRD_PARTY.test(file));
    for (const file of files) {
      result.set(file.replace(/\\/g, '/'), null);
    }
    return result;
  }
  const diff = git(`diff ${staged ? '--cached ' : ''}-U0 --no-color`);
  let current = null;
  for (const line of diff.split('\n')) {
    if (line.startsWith('+++ ')) {
      current = line.startsWith('+++ b/') ? line.slice(6) : null;
      if (current && !result.has(current)) {
        result.set(current, new Set());
      }
      continue;
    }
    const hunk = /^@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@/.exec(line);
    if (hunk && current) {
      const start = Number(hunk[1]);
      const count = hunk[2] === undefined ? 1 : Number(hunk[2]);
      for (let i = 0; i < count; i++) {
        result.get(current).add(start + i);
      }
    }
  }
  if (!staged) {
    for (const file of git('ls-files --others --exclude-standard').split('\n')) {
      if (file) {
        result.set(file, null);
      }
    }
  }
  return result;
}

function stripStrings(line) {
  return line.replace(/@"(?:[^"]|"")*"/g, '""').replace(/\$?"(?:[^"\\]|\\.)*"/g, '""').replace(/'(?:[^'\\]|\\.)'/g, "''");
}

function depth(text) {
  let value = 0;
  for (const char of text) {
    if (char === '(') {
      value++;
    } else if (char === ')') {
      value--;
    }
  }
  return value;
}

const MEMBER = /^\s*(?:\[[^\]]*\]\s*)*(public|private|protected|internal)\s+((?:static\s+|readonly\s+|const\s+|new\s+|override\s+|virtual\s+|abstract\s+|sealed\s+|extern\s+|unsafe\s+|volatile\s+|event\s+)*)([\w<>\[\],.?\s]+?)\s+(\w+)\s*(=>|=|;|\{)/;

function memberInfo(line) {
  const match = MEMBER.exec(line);
  if (!match || /\b(class|struct|interface|enum|record|delegate)\b/.test(match[3]) || /\(/.test(line.split(/=>|=(?!=)/)[0])) {
    return null;
  }
  const modifiers = match[2];
  const serialized = /\[SerializeField\]/.test(line);
  const isConst = /\bconst\b/.test(modifiers);
  const isStatic = /\bstatic\b/.test(modifiers);
  const isReadonly = /\breadonly\b/.test(modifiers);
  const kind = match[5] === '=>' || match[5] === '{' ? 'property' : 'field';
  const isEvent = /\bevent\b/.test(modifiers);
  return { access: match[1], name: match[4], isConst, isStatic, isReadonly, isEvent, serialized, kind, key: [match[1], isConst, isStatic, isReadonly, serialized, kind].join('|') };
}

function check(file, lines) {
  const extension = path.extname(file).toLowerCase();
  const csharp = extension === '.cs';
  const full = path.join(project, file);
  if (!fs.existsSync(full) || !CODE_EXTENSIONS.test(file)) {
    return [];
  }
  const text = fs.readFileSync(full, 'utf8').split(/\r?\n/);
  const issues = [];
  const report = (index, rule) => {
    if (lines === null || lines.has(index + 1)) {
      issues.push(`${file}:${index + 1}: ${rule}: ${text[index].trim().slice(0, 140)}`);
    }
  };
  let previousMember = null;
  let blankRun = 0;
  for (let i = 0; i < text.length; i++) {
    const raw = text[i];
    const code = stripStrings(raw);
    const trimmed = code.trim();
    if (i === 0 && trimmed.startsWith('#!')) {
      continue;
    }
    if (/(^|[^:\\])\/\/|(^|[^\\])\/\*/.test(code) && !/https?:\/\//.test(raw)) {
      report(i, 'comment');
    }
    if ((extension === '.sh' || extension === '.ps1') && /^#(?!!)/.test(trimmed)) {
      report(i, 'comment');
    }
    if (!csharp) {
      continue;
    }
    if (/^(if|else if|while|for|foreach|switch|using|lock)\s*\(/.test(trimmed) && depth(trimmed) > 0) {
      report(i, 'condition split across lines');
    } else if (depth(trimmed) > 0 && /\(\s*$|,\s*$/.test(trimmed)) {
      report(i, 'parameters split across lines');
    }
    if (trimmed.length > 1 && trimmed.endsWith('{') && !/(=>|=|\bnew\b[^;]*|\(|,|\[\])\s*\{$/.test(trimmed)) {
      report(i, 'brace must be on its own line');
    }
    if (trimmed === '') {
      blankRun++;
      continue;
    }
    const member = memberInfo(code);
    if (member) {
      if (member.isConst && !/^[A-Z][A-Z0-9_]*$/.test(member.name)) {
        report(i, 'const must be UPPER_CASE');
      }
      if (member.kind === 'field' && !member.isConst && (member.access === 'private' || member.serialized) && !member.name.startsWith('_') && !(member.isStatic && member.isReadonly && /^[A-Z][A-Z0-9_]*$/.test(member.name))) {
        report(i, 'private field must start with _');
      }
      if (member.kind === 'field' && member.access === 'public' && !member.isConst && !member.isStatic && !member.isReadonly && !member.isEvent) {
        report(i, 'warning: public mutable field (ok only for JsonUtility DTOs)');
      }
      member.single = trimmed.endsWith(';');
      if (previousMember && blankRun > 0 && previousMember.key === member.key && previousMember.single && member.single) {
        report(i, 'blank line between members of the same group');
      }
      previousMember = member;
    } else if (!/^\[/.test(trimmed)) {
      previousMember = null;
    }
    blankRun = 0;
  }
  return issues;
}

const targets = addedLines();
let total = 0;
for (const [file, lines] of targets) {
  if (lines !== null && lines.size === 0) {
    continue;
  }
  const issues = check(file, lines);
  total += issues.filter(issue => !issue.includes(': warning: ')).length;
  issues.forEach(issue => console.log(issue));
}
console.log(`style check: ${total} issue(s) in ${targets.size} file(s)${all ? ' (all tracked .cs)' : explicit.length ? '' : staged ? ' (staged diff)' : ' (working diff + untracked)'}`);
process.exit(total > 0 ? 1 : 0);
