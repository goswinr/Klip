import { expect, test } from 'vitest';
import { makePath } from './adapter';
// @ts-ignore -- generated Fable module
import { xAndYSingle } from '../_js/Src/Snap.js';

test('snap candidates and cluster means are invariant under winding and start vertex', () => {
  const vertices = [[0, 0], [0.75, 10], [1.5, 20], [10, 30]];
  for (const winding of [vertices, [...vertices].reverse()]) {
    for (let start = 0; start < 4; start++) {
      const p = makePath([...winding.slice(start), ...winding.slice(0, start)].flat());
      xAndYSingle(1, [p]);
      for (let i = 0; i < 8; i += 2) {
        expect(p.xys[i]).toBe(p.xys[i + 1] <= 10 ? 0.375 : p.xys[i + 1] === 20 ? 1.5 : 10);
      }
    }
  }
});

test.each([0, 1e-5])('snap tolerance %s preserves identical large coordinates', tolerance => {
  for (const x of [1e12 + 0.1, 1e308]) {
    const ps = Array.from({ length: 5 }, () => makePath([x, 0, x, 10, x, 20]));
    xAndYSingle(tolerance, ps);
    for (const p of ps) expect([p.xys[0], p.xys[2], p.xys[4]]).toEqual([x, x, x]);
  }
});

test('snap validates all inputs before writing any coordinates', () => {
  for (const bad of [-1, NaN, Infinity]) expect(() => xAndYSingle(bad, [])).toThrow();
  const p = makePath([0, 0, 0.5, 10, 10, 20]);
  const before = [...p.xys];
  expect(() => xAndYSingle(1, [p, makePath([NaN, 0, 1, 1])])).toThrow();
  expect(p.xys).toEqual(before);
});
