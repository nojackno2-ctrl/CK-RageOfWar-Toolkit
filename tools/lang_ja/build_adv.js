const fs = require('fs');
const path = require('path');

const srcPath = path.join(__dirname, '../../assets/langpacks/zh-TW/campaign-celtic-kings-adventure.json');
const destPath = path.join(__dirname, '../../assets/langpacks/ja-JP/campaign-celtic-kings-adventure.json');

const twData = JSON.parse(fs.readFileSync(srcPath, 'utf8').replace(/^\uFEFF/, ''));

const ui = JSON.parse(fs.readFileSync(path.join(__dirname, '../../assets/langpacks/ja-JP/ui.json'), 'utf8'));
const tut = JSON.parse(fs.readFileSync(path.join(__dirname, '../../assets/langpacks/ja-JP/campaign-tutorial.json'), 'utf8'));
const help = JSON.parse(fs.readFileSync(path.join(__dirname, '../../assets/langpacks/ja-JP/help.json'), 'utf8'));

const part1 = require('./data_adv_1.js');
const part2 = require('./data_adv_2.js');
const part3 = require('./data_adv_3.js');
const part4 = require('./data_adv_4.js');

const miss1 = require('./data_adv_missing_1.js');
const miss2 = require('./data_adv_missing_2.js');
const miss3 = require('./data_adv_missing_3.js');
const miss4 = require('./data_adv_missing_4.js');
const miss5 = require('./data_adv_missing_5.js');

const combined = Object.assign({}, ui, tut, help, part1, part2, part3, part4, miss1, miss2, miss3, miss4, miss5);

// Normalized map for newlines and trimming
const normMap = new Map();
for (const [k, v] of Object.entries(combined)) {
  const normKey = k.replace(/\r\n/g, '\n').trim();
  normMap.set(normKey, v);
}

const missing = [];
const result = {};

for (const [key, twVal] of Object.entries(twData)) {
  if (key.startsWith('NO_') || key === 'Haemimont Games') {
    result[key] = key;
  } else if (combined[key] !== undefined) {
    result[key] = combined[key];
  } else {
    const normKey = key.replace(/\r\n/g, '\n').trim();
    if (normMap.has(normKey)) {
      result[key] = normMap.get(normKey);
    } else {
      missing.push({ key, twVal });
    }
  }
}

console.log(`Matched: ${Object.keys(result).length} / ${Object.keys(twData).length}`);
if (missing.length > 0) {
  console.log(`Missing ${missing.length} keys:`);
  missing.forEach((m, idx) => console.log(`${idx}: ${JSON.stringify(m.key)} => ${JSON.stringify(m.twVal)}`));
} else {
  fs.writeFileSync(destPath, JSON.stringify(result, null, 2), 'utf8');
  console.log(`Successfully generated ${destPath}`);
}
