import type { KlipPaths64 } from './adapter';
import type { ClipType, FillRule } from './test-data-parser';
// @ts-ignore -- Fable JavaScript has no accompanying .d.ts
import * as Engine from '../_js/Src/Engine.js';

/** The upstream integer-grid fixture counts include these historical culls.
 * Set them explicitly instead of requiring float defaults to discard small contours.
 * The same fixtures also run through the default bundle with their area assertions.
 */
export function booleanOpIntegerFixture(
  clipType: ClipType, subject: KlipPaths64, clip: KlipPaths64 | null, fillRule: FillRule,
): KlipPaths64 {
  const c = Engine.Clipper64$1_$ctor();
  Engine.Clipper64$1__set_NearTopYToleranceCap_5E38073B(c, 2);
  Engine.Clipper64$1__set_SmallTriangleTolerance_5E38073B(c, 2);
  Engine.Clipper64$1__set_SplitAreaTolerance_5E38073B(c, 2);
  Engine.Clipper64$1__AddSubject_2ABD14E4(c, subject);
  if (clip !== null) Engine.Clipper64$1__AddClip_2ABD14E4(c, clip);
  return Engine.Clipper64$1__Execute_Z140889D1(c, clipType, fillRule)[0];
}
