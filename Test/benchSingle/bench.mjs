import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath, pathToFileURL } from "node:url";
import createClipper2Wasm from "clipper2-wasm";

const require = createRequire(import.meta.url);
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const CLIP_PRECISIONS = [4]; //[1, 3, 6];
const LIP_FILL_RULE_NON_ZERO = 1;

function flatten(polys) {
  const paths = [];
  for (const poly of polys) {
    for (const ring of poly) {
      paths.push(ring);
    }
  }
  return paths;
}

async function main() {
  const { Bench } = await import("tinybench");
  const { union, FillRule, areaPaths, scalePaths64 } = await import("clipper2-ts");
  const clipper2Wasm = await loadClipper2Wasm();
  const lipModulePath = pathToFileURL(path.join(__dirname, "..", "_dist", "Klip.mjs")).href;
  const lipModulePathPerf = pathToFileURL(path.join(__dirname, "..", "_distPerf", "Klip.mjs")).href;
  const { unionSelf: unionSelfLip } = await import(lipModulePath);
  const { unionSelf: unionSelfLipPerf } = await import(lipModulePathPerf);

  const clipperDataPath = path.join(__dirname, "polysXY.json"); // for clipper2-ts

  const pathsXY = flatten(JSON.parse(fs.readFileSync(clipperDataPath, "utf8")));
  const pathsClipper64ByPrecision = new Map(
    CLIP_PRECISIONS.map((precision) => [
      precision,
      scalePaths64(pathsXY, scaleForPrecision(precision)),
    ]),
  );
  const pathsClipper2WasmByPrecision = new Map(
    CLIP_PRECISIONS.map((precision) => [
      precision,
      toClipper2WasmPaths(pathsXY, scaleForPrecision(precision), clipper2Wasm),
    ]),
  );
  const pathsLipByPrecision = new Map(
    CLIP_PRECISIONS.map((precision) => [precision, toLipPaths(pathsXY, scaleForPrecision(precision))]),
  );

  console.log(`Loaded ${pathsXY.length} polygons for clipper2-ts (${pathsXY.length} paths)`);
  console.log(`Loaded ${pathsXY.length} polygons for clipper2-wasm (${pathsXY.length} paths)`);
  console.log(`Loaded Klip paths for clip precisions ${CLIP_PRECISIONS.join(", ")}`);

  const bench = new Bench({
    time: 500,
    warmupTime: 50,
  });

  const taskInfo = new Map();

  for (const precision of CLIP_PRECISIONS) {
    const pathsClipper64 = pathsClipper64ByPrecision.get(precision);
    const pathsClipper2Wasm = pathsClipper2WasmByPrecision.get(precision);
    const pathsLip = pathsLipByPrecision.get(precision);
    const clipperTaskName = `clipper2-ts union all polygons p=${precision}`;
    const clipper2WasmTaskName = `clipper2-wasm union all polygons p=${precision}`;
    const lipTaskName = `Klip union all polygons p=${precision}`;
    const lipPerfTaskName = `LipPerf union all polygons p=${precision}`;

    taskInfo.set(clipperTaskName, { engine: "clipper2-ts", precision });
    taskInfo.set(clipper2WasmTaskName, { engine: "clipper2-wasm", precision });
    taskInfo.set(lipTaskName, { engine: "Klip", precision });
    taskInfo.set(lipPerfTaskName, { engine: "LipPerf", precision });

    bench.add(clipperTaskName, () => {
      const result = union(pathsClipper64, FillRule.NonZero);
      if (!result || result.length === 0) {
        throw new Error(`clipper2-ts returned no polygons for precision ${precision}`);
      }
    });

    bench.add(clipper2WasmTaskName, () => {
      const result = clipper2Wasm.UnionSelf64(pathsClipper2Wasm, clipper2Wasm.FillRule.NonZero);
      try {
        if (!result || result.size() === 0) {
          throw new Error(`clipper2-wasm returned no polygons for precision ${precision}`);
        }
      } finally {
        result?.delete?.();
      }
    });

    bench.add(lipPerfTaskName, () => {
      const result = unionSelfLipPerf(pathsLip, LIP_FILL_RULE_NON_ZERO);
      if (!result || result.length === 0) {
        throw new Error(`LipPerf returned no polygons for precision ${precision}`);
      }
    });

    bench.add(lipTaskName, () => {
      const result = unionSelfLip(pathsLip, LIP_FILL_RULE_NON_ZERO);
      if (!result || result.length === 0) {
        throw new Error(`Klip returned no polygons for precision ${precision}`);
      }
    });
  }

  await bench.run();

  const results = new Map();
  const clipperHzByPrecision = new Map();

  for (const precision of CLIP_PRECISIONS) {
    const scale = scaleForPrecision(precision);
    const pathsClipper64 = pathsClipper64ByPrecision.get(precision);
    const pathsClipper2Wasm = pathsClipper2WasmByPrecision.get(precision);
    const pathsLip = pathsLipByPrecision.get(precision);
    const clipperTaskName = `clipper2-ts union all polygons p=${precision}`;
    const clipper2WasmTaskName = `clipper2-wasm union all polygons p=${precision}`;
    const lipTaskName = `Klip union all polygons p=${precision}`;
    const lipPerfTaskName = `LipPerf union all polygons p=${precision}`;
    const clipperResult = union(pathsClipper64, FillRule.NonZero);
    const clipper2WasmResult = clipper2Wasm.UnionSelf64(pathsClipper2Wasm, clipper2Wasm.FillRule.NonZero);
    const lipResult = unionSelfLip(pathsLip, LIP_FILL_RULE_NON_ZERO);
    const lipPerfResult = unionSelfLipPerf(pathsLip, LIP_FILL_RULE_NON_ZERO);

    results.set(clipperTaskName, {
      polygons: clipperResult.length,
      area: areaPaths(clipperResult) / (scale * scale),
    });
    try {
      results.set(clipper2WasmTaskName, {
        polygons: clipper2WasmResult.size(),
        area: clipper2Wasm.AreaPaths64(clipper2WasmResult) / (scale * scale),
      });
    } finally {
      clipper2WasmResult.delete();
    }
    results.set(lipTaskName, {
      polygons: lipResult.length,
      area: areaFromLipPaths(lipResult, scale),
    });
    results.set(lipPerfTaskName, {
      polygons: lipPerfResult.length,
      area: areaFromLipPaths(lipPerfResult, scale),
    });

    const clipperTask = bench.tasks.find((task) => task.name === clipperTaskName);
    clipperHzByPrecision.set(precision, clipperTask?.result?.hz ?? 0);
  }

  console.table(
    bench.tasks.map((task) => {
      const info = taskInfo.get(task.name);
      const clipperHz = clipperHzByPrecision.get(info.precision) ?? 0;
      const taskResult = results.get(task.name);

      return {
        precision: info.precision,
        engine: info.engine,
        "ops/s": task.result?.hz?.toFixed(2),
        "avg ms": task.result ? (1000 / task.result.hz).toFixed(4) : "",
        "vs clipper2-ts": clipperHz && task.result?.hz ? `${(task.result.hz / clipperHz).toFixed(2)}x` : "",
        samples: task.result?.samples?.length ?? 0,
        polygons: taskResult?.polygons ?? "",
        area: taskResult?.area?.toFixed(6) ?? "",
      };
    }),
  );

  for (const pathsClipper2Wasm of pathsClipper2WasmByPrecision.values()) {
    pathsClipper2Wasm.delete();
  }
}

async function loadClipper2Wasm() {
  const clipper2WasmEntrypoint = require.resolve("clipper2-wasm");
  const clipper2WasmDirectory = path.dirname(clipper2WasmEntrypoint);
  const clipper2WasmBinary = fs.readFileSync(path.join(clipper2WasmDirectory, "clipper2z.wasm"));

  return createClipper2Wasm({ wasmBinary: clipper2WasmBinary });
}

function scaleForPrecision(precision) {
  return 10 ** precision;
}

function toLipPaths(pathsXY, scale) {
  return pathsXY.map((ring) => createLipPath(ring, scale));
}

function toClipper2WasmPaths(pathsXY, scale, clipper2Wasm) {
  const paths = new clipper2Wasm.Paths64();

  for (const ring of pathsXY) {
    const flatRing = [];
    for (const point of ring) {
      flatRing.push(Math.round(point.x * scale), Math.round(point.y * scale));
    }

    const clipper2WasmPath = clipper2Wasm.MakePath64(flatRing);
    paths.push_back(clipper2WasmPath);
    clipper2WasmPath.delete();
  }

  return paths;
}

function areaFromLipPaths(paths, scale) {
  let area = 0;
  for (const ring of paths) {
    area += Math.abs(ringAreaFromArrays(ring.Xs ?? ring.xs, ring.Ys ?? ring.ys));
  }
  return area / (scale * scale);
}

function createLipPath(ring, scale) {
  const xs = ring.map((point) => Math.round(point.x * scale));
  const ys = ring.map((point) => Math.round(point.y * scale));
  const zs = new Array(ring.length).fill(0);

  return {
    xs,
    ys,
    zs,
    get Xs() {
      return this.xs;
    },
    get Ys() {
      return this.ys;
    },
    get Zs() {
      return this.zs;
    },
    get Count() {
      return this.xs.length;
    },
  };
}

function ringAreaFromArrays(xs, ys) {
  let sum = 0;
  const count = xs.length;
  for (let i = 0; i < count; i++) {
    const j = (i + 1) % count;
    sum += xs[i] * ys[j] - xs[j] * ys[i];
  }
  return sum / 2;
}

function ringArea(ring) {
  let sum = 0;
  for (let i = 0; i < ring.length - 1; i++) {
    const [x1, y1] = ring[i];
    const [x2, y2] = ring[i + 1];
    sum += x1 * y2 - x2 * y1;
  }
  return sum / 2;
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});