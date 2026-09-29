import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { describe, expect, it } from "vitest";

/** Walks up from the test's working directory so the token file is found from anywhere the suite runs. */
function repositoryFile(relative: string): string {
  let directory = process.cwd();
  for (;;) {
    const candidate = join(directory, relative);
    if (existsSync(candidate)) return candidate;
    const parent = dirname(directory);
    if (parent === directory) throw new Error(`Could not find ${relative} from ${process.cwd()}.`);
    directory = parent;
  }
}

const tokens = readFileSync(repositoryFile("design/prototest-tokens.css"), "utf8");

function block(marker: string): string {
  const start = tokens.indexOf(marker);
  if (start < 0) throw new Error(`No ${marker} block in the token file.`);
  return tokens.slice(start, tokens.indexOf("}", start));
}

function value(theme: string, name: string): string {
  const match = new RegExp(`--${name}:\\s*(#[0-9a-fA-F]{6})`).exec(block(`color-scheme: ${theme};`));
  if (!match) throw new Error(`No --${name} in the ${theme} theme.`);
  return match[1];
}

function luminance(hex: string): number {
  const channel = (offset: number) => {
    const value = parseInt(hex.slice(offset, offset + 2), 16) / 255;
    return value <= 0.03928 ? value / 12.92 : ((value + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * channel(1) + 0.7152 * channel(3) + 0.0722 * channel(5);
}

function contrast(foreground: string, background: string): number {
  const [high, low] = [luminance(foreground), luminance(background)].sort((left, right) => right - left);
  return (high + 0.05) / (low + 0.05);
}

// Tertiary text sits on hovered and tinted rows across the viewer, the report and the docs, so its
// contrast is a property of the one token file, not of any single screen.
describe("design tokens", () => {
  it("keeps tertiary text at WCAG AA on the hover surface in both themes", () => {
    for (const theme of ["light", "dark"]) {
      expect(contrast(value(theme, "dim"), value(theme, "hover"))).toBeGreaterThanOrEqual(4.5);
    }
  });

  it("keeps tertiary text a step away from secondary text in both themes", () => {
    expect(luminance(value("light", "dim"))).toBeGreaterThan(luminance(value("light", "muted")));
    expect(luminance(value("dark", "dim"))).toBeLessThan(luminance(value("dark", "muted")));
  });
});
