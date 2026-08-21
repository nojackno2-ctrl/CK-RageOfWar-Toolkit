const fs = require('fs');
const path = require('path');

const files = [
  'help.json',
  'campaign-tutorial.json',
  'ui.json',
  'campaign-celtic-kings-adventure.json'
];

let allPassed = true;

for (const f of files) {
  const twPath = path.join(__dirname, '../../assets/langpacks/zh-TW', f);
  const jaPath = path.join(__dirname, '../../assets/langpacks/ja-JP', f);
  
  const tw = JSON.parse(fs.readFileSync(twPath, 'utf8').replace(/^\uFEFF/, ''));
  const ja = JSON.parse(fs.readFileSync(jaPath, 'utf8').replace(/^\uFEFF/, ''));
  
  const twKeys = Object.keys(tw);
  const jaKeys = Object.keys(ja);
  
  console.log(`=== Checking ${f} ===`);
  console.log(`  Source keys: ${twKeys.length}`);
  console.log(`  Target keys: ${jaKeys.length}`);
  
  if (twKeys.length !== jaKeys.length) {
    console.error(`  ERROR: Key count mismatch!`);
    allPassed = false;
  }
  
  const missingKeys = twKeys.filter(k => ja[k] === undefined);
  if (missingKeys.length > 0) {
    console.error(`  ERROR: Missing keys in JA: ${missingKeys.length}`);
    allPassed = false;
  }
  
  // Check for untranslated values
  let untranslated = [];
  for (const [k, v] of Object.entries(ja)) {
    if (k.startsWith('NO_') || k === 'Haemimont Games') continue;
    if (k === v && /[A-Za-z]{3,}/.test(v)) {
      untranslated.push({ k, v });
    }
  }
  
  if (untranslated.length > 0) {
    console.warn(`  WARNING: Possible untranslated (${untranslated.length} entries):`);
    untranslated.forEach(u => console.warn(`    ${u.k} => ${u.v}`));
    allPassed = false;
  } else {
    console.log(`  Untranslated check: 0 untranslated strings found. (100% Translated)`);
  }
}

console.log('=== Pack Manifest Check ===');
const pack = JSON.parse(fs.readFileSync(path.join(__dirname, '../../assets/langpacks/ja-JP/pack.json'), 'utf8').replace(/^\uFEFF/, ''));
console.log('pack.json valid:', pack.id, pack.name, pack.nativeName, pack.version);
console.log('ALL VERIFICATION PASSED:', allPassed);
