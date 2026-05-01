import { describe, test, expect } from 'vitest';
import {
  LipInternal_ScanlineHeap_$ctor,
  LipInternal_ScanlineHeap__Push_5E38073B,
  LipInternal_ScanlineHeap__Pop,
  LipInternal_ScanlineHeap__ClearData
} from '../_ts/Src/Engine1.ts';

describe('ScanlineHeap', () => {
  test('pops scanlines in descending order', () => {
    const heap = LipInternal_ScanlineHeap_$ctor();

    for (const value of [5, 1, 4, 2, 3]) {
      LipInternal_ScanlineHeap__Push_5E38073B(heap, value);
    }

    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(5);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(4);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(3);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(2);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(1);
    expect(Number.isNaN(LipInternal_ScanlineHeap__Pop(heap))).toBe(true);
  });

  test('push after pop keeps the heap ordered', () => {
    const heap = LipInternal_ScanlineHeap_$ctor();

    LipInternal_ScanlineHeap__Push_5E38073B(heap, 4);
    LipInternal_ScanlineHeap__Push_5E38073B(heap, 2);
    LipInternal_ScanlineHeap__Push_5E38073B(heap, 3);

    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(4);

    LipInternal_ScanlineHeap__Push_5E38073B(heap, 1);

    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(3);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(2);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(1);
  });

  test('clear resets the heap for reuse', () => {
    const heap = LipInternal_ScanlineHeap_$ctor();

    LipInternal_ScanlineHeap__Push_5E38073B(heap, 9);
    LipInternal_ScanlineHeap__Push_5E38073B(heap, 7);
    LipInternal_ScanlineHeap__ClearData(heap);

    expect(Number.isNaN(LipInternal_ScanlineHeap__Pop(heap))).toBe(true);

    LipInternal_ScanlineHeap__Push_5E38073B(heap, 6);
    LipInternal_ScanlineHeap__Push_5E38073B(heap, 8);

    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(8);
    expect(LipInternal_ScanlineHeap__Pop(heap)).toBe(6);
    expect(Number.isNaN(LipInternal_ScanlineHeap__Pop(heap))).toBe(true);
  });
});