

// run with `node ./test/Union-Touching/test-on-polysXY.mjs`

import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import { Klipper_unionSelfChecked } from "../_dist/Klip.mjs";

const scriptDir = dirname(fileURLToPath(import.meta.url));
const dataPath = resolve(scriptDir, "../Rhino/polysXY.json");

function toKlipPath(path) {
  const xys = new Array(path.length * 2);

  for (let i = 0; i < path.length; i++) {
    const pt = path[i];
    const coord = i * 2;
    xys[coord] = pt.x;
    xys[coord + 1] = pt.y;
  }

  return { xys };
}

const xyGroups = JSON.parse(await readFile(dataPath, "utf8"));

if (!Array.isArray(xyGroups)) {
  throw new Error("Expected polysXY.json to contain an array of polygon groups.");
}

const xy = xyGroups.map((group, index) => {
  if (!Array.isArray(group) || group.length === 0 || !Array.isArray(group[0])) {
    throw new Error(`Expected polygon group ${index} to contain at least one path.`);
  }

  return group[0];
});

console.log(`Original Paths: ${xy.length}`);

const kr = Klipper_unionSelfChecked(xy.map(toKlipPath));
console.log(`Klip Result Paths: ${kr.length}`);
