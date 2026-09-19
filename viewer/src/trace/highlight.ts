import Prism from "prismjs";
import "prismjs/components/prism-clike";
import "prismjs/components/prism-csharp";
import "prismjs/components/prism-json";
import "prismjs/components/prism-javascript";
import "prismjs/components/prism-typescript";

/** One run of text on one line, with the Prism token types it sits inside (outermost first). */
export interface HighlightedPart {
  text: string;
  types: string[];
}

const languages: Record<string, string> = {
  cs: "csharp", json: "json", js: "javascript", mjs: "javascript", ts: "typescript", tsx: "typescript"
};

export function languageOf(path: string): string | undefined {
  return languages[path.split(".").pop()?.toLowerCase() ?? ""];
}

/**
 * Tokenises code the way the documentation's code blocks do (Prism, the same grammars) and splits it into lines,
 * so a view can number and mark them. Unknown languages come back as plain lines.
 */
export function highlightLines(code: string, language: string | undefined): HighlightedPart[][] {
  const grammar = language ? Prism.languages[language] : undefined;
  const lines: HighlightedPart[][] = [[]];
  const push = (text: string, types: string[]) => {
    text.split("\n").forEach((piece, index) => {
      if (index > 0) lines.push([]);
      if (piece) lines[lines.length - 1].push({ text: piece, types });
    });
  };
  const walk = (stream: Prism.TokenStream, types: string[]) => {
    if (typeof stream === "string") { push(stream, types); return; }
    if (Array.isArray(stream)) { stream.forEach(part => walk(part, types)); return; }
    const aliases = stream.alias ? (Array.isArray(stream.alias) ? stream.alias : [stream.alias]) : [];
    walk(stream.content, [...types, stream.type, ...aliases]);
  };
  walk(grammar ? Prism.tokenize(code, grammar) : code, []);
  return lines;
}
