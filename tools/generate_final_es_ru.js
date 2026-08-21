const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const twAdv = JSON.parse(fs.readFileSync(path.join(ROOT, 'assets/langpacks/zh-TW/campaign-celtic-kings-adventure.json'), 'utf8').replace(/^\uFEFF/, ''));
const itAdv = JSON.parse(fs.readFileSync(path.join(ROOT, 'assets/langpacks/it-IT/campaign-celtic-kings-adventure.json'), 'utf8').replace(/^\uFEFF/, ''));

console.log('Total entries:', Object.keys(twAdv).length);

// Let's create an exact dictionary for Spanish (Imperivm / Celtic Kings)
const esAdv = {};
const ruAdv = {};

// We can load what the subagents already produced
try {
  const p1 = require(path.join(ROOT, 'adv_part1.js'));
  const p2 = require(path.join(ROOT, 'adv_part2.js'));
  const p3 = require(path.join(ROOT, 'adv_part3.js'));
  const p4 = require(path.join(ROOT, 'adv_part4.js'));
  Object.assign(esAdv, p1, p2, p3, p4);
} catch (e) {}

console.log('Pre-loaded ES keys:', Object.keys(esAdv).length);
