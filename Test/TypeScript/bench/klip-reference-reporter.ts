import { BenchmarkReporter } from 'vitest/node';

interface BenchmarkStats {
  hz?: number;
}

interface BenchmarkTask {
  name: string;
  type?: string;
  tasks?: BenchmarkTask[];
  meta?: { benchmark?: boolean };
  result?: { benchmark?: BenchmarkStats };
}

const referenceName = 'Klip';
const useColor = process.env.NO_COLOR === undefined;

function color(code: string, text: string): string {
  return useColor ? `\u001B[${code}m${text}\u001B[0m` : text;
}

function bold(text: string): string {
  return color('1', text);
}

function cyan(text: string): string {
  return color('36', text);
}

function gray(text: string): string {
  return color('90', text);
}

function green(text: string): string {
  return color('32', text);
}

function red(text: string): string {
  return color('31', text);
}

function ratioColor(ratio: number, text: string): string {
  return ratio >= 1 ? green(text) : red(text);
}

function collectBenchmarkGroups(task: BenchmarkTask, path: string[] = []): { name: string; tasks: BenchmarkTask[] }[] {
  const groups: { name: string; tasks: BenchmarkTask[] }[] = [];
  const children = task.tasks ?? [];
  const benchmarkTasks = children.filter(child => child.meta?.benchmark && child.result?.benchmark);

  if (benchmarkTasks.length > 0) {
    groups.push({
      name: [...path, task.name].filter(Boolean).join(' > '),
      tasks: benchmarkTasks,
    });
  }

  for (const child of children) {
    if (child.type === 'suite' || child.tasks) {
      groups.push(...collectBenchmarkGroups(child, [...path, task.name].filter(Boolean)));
    }
  }

  return groups;
}

function padName(name: string, tasks: BenchmarkTask[]): string {
  const width = Math.max(referenceName.length, ...tasks.map(task => task.name.length));
  return name.padEnd(width, ' ');
}

export default class KlipReferenceReporter extends BenchmarkReporter {
  reportBenchmarkSummary(files: BenchmarkTask[]): void {
    this.log(`\n ${cyan(bold('BENCH'))}  ${bold('Summary')} ${gray('(Klip reference)')}\n`);

    for (const file of files) {
      for (const group of collectBenchmarkGroups(file)) {
        const reference = group.tasks.find(task => task.name === referenceName);
        const referenceHz = reference?.result?.benchmark?.hz;

        if (!reference || !referenceHz) continue;

        this.log(`  ${green(referenceName)} ${gray(`- ${group.name}`)}`);
        this.log(`    ${green(bold(padName(referenceName, group.tasks)))}  ${green(bold('1.00x'))} ${gray('reference')}`);

        for (const task of group.tasks) {
          if (task === reference) continue;

          const hz = task.result?.benchmark?.hz;
          if (!hz) continue;

          const ratio = hz / referenceHz;
          this.log(`    ${padName(task.name, group.tasks)}  ${ratioColor(ratio, `${ratio.toFixed(2)}x`)} ${gray(`vs ${referenceName}`)}`);
        }

        this.log('');
      }
    }
  }
}