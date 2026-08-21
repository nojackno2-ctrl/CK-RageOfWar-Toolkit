const fs = require('fs');
const path = require('path');

const srcPath = path.join(__dirname, '../../assets/langpacks/zh-TW/ui.json');
const destPath = path.join(__dirname, '../../assets/langpacks/ja-JP/ui.json');

const twData = JSON.parse(fs.readFileSync(srcPath, 'utf8').replace(/^\uFEFF/, ''));

const part1 = require('./data_ui_1.js');
const part2 = require('./data_ui_2.js');
const part3 = require('./data_ui_3.js');
const part4 = require('./data_ui_4.js');
const part5 = require('./data_ui_5.js');

const combined = Object.assign({}, part1, part2, part3, part4, part5);

// Build normalized dictionary for robust newline matching
const normMap = new Map();
for (const [k, v] of Object.entries(combined)) {
  const normKey = k.replace(/\r\n/g, '\n').trim();
  normMap.set(normKey, v);
}

const missing = [];
const result = {};
for (const key of Object.keys(twData)) {
  if (combined[key] !== undefined) {
    result[key] = combined[key];
  } else {
    const normKey = key.replace(/\r\n/g, '\n').trim();
    if (normMap.has(normKey)) {
      result[key] = normMap.get(normKey);
    } else {
      missing.push(key);
    }
  }
}

console.log(`Matched: ${Object.keys(result).length} / ${Object.keys(twData).length}`);
if (missing.length > 0) {
  console.log(`Missing ${missing.length} keys:`);
  missing.forEach((k, idx) => console.log(`${idx}: ${JSON.stringify(k)}`));
} else {
  fs.writeFileSync(destPath, JSON.stringify(result, null, 2), 'utf8');
  console.log(`Successfully generated ${destPath}`);
}
