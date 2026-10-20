const fs = require('fs');
const file = process.argv[2];
const xml = fs.readFileSync(file, 'utf8');
const run = /<test-run [^>]*>/.exec(xml);
if (run) {
  const attr = name => (new RegExp(name + '="([^"]*)"').exec(run[0]) || [])[1];
  console.log(`total ${attr('total')} passed ${attr('passed')} failed ${attr('failed')} skipped ${attr('skipped')} duration ${attr('duration')}s`);
}
const cases = /<test-case [^>]*?fullname="([^"]*)"[^>]*?result="(\w+)"[^>]*>([\s\S]*?)<\/test-case>/g;
let match;
while ((match = cases.exec(xml))) {
  const [, name, result, body] = match;
  if (result === 'Passed') {
    continue;
  }
  const message = (/<message><!\[CDATA\[([\s\S]*?)\]\]>/.exec(body) || [])[1] || '';
  const stack = (/<stack-trace><!\[CDATA\[([\s\S]*?)\]\]>/.exec(body) || [])[1] || '';
  console.log(`${result.toUpperCase()} ${name.replace(/&quot;/g, '"')}`);
  if (message) {
    console.log('  ' + message.trim().split('\n').slice(0, 4).join('\n  '));
  }
  const location = stack.split('\n').find(line => line.includes('.cs:'));
  if (location && result === 'Failed') {
    console.log('  ' + location.trim());
  }
}
